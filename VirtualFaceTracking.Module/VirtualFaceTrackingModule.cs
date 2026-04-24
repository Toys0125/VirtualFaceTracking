using System.Diagnostics;
using VirtualFaceTracking.Module.Runtime;
using VirtualFaceTracking.Shared;
using VirtualFaceTracking.Shared.Abstractions;
using VirtualFaceTracking.Shared.Diagnostics;
using VirtualFaceTracking.Shared.IPC;
using VirtualFaceTracking.Shared.Mapping;
using VirtualFaceTracking.Shared.Persistence;
using VirtualFaceTracking.Shared.Simulation;
using VRCFaceTracking;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Types;

namespace VirtualFaceTracking.Module;

public sealed class VirtualFaceTrackingModule : ExtTrackingModule
{
    private readonly object _stateLock = new();
    private readonly Dictionary<string, UnifiedExpressions> _shapeLookup = Enum
        .GetValues<UnifiedExpressions>()
        .Where(value => value != UnifiedExpressions.Max)
        .ToDictionary(value => value.ToString(), value => value, StringComparer.Ordinal);

    private readonly IVirtualSimulationEngine _simulationEngine = new VirtualSimulationEngine();
    private readonly IVirtualExpressionMapper _expressionMapper = new VirtualExpressionMapper();

    private ModulePaths? _paths;
    private IVirtualTrackerStateStore? _stateStore;
    private NamedPipeModuleServer? _pipeServer;
    private TrackerRuntimeState _state = new();
    private Stopwatch _stopwatch = Stopwatch.StartNew();
    private TimeSpan _lastUpdate;
    private bool? _lastActiveOutput;
    private string _lastOutputReason = string.Empty;
    private DateTimeOffset _lastHeartbeatLogUtc = DateTimeOffset.MinValue;

    public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);

    public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
    {
        _paths = ModulePaths.Resolve();
        Directory.CreateDirectory(_paths.DeploymentDirectory);
        VirtualTrackerDiagnostics.Configure(_paths.DeploymentDirectory);
        VirtualTrackerDiagnostics.Write("Module", $"Initialize requested. EyeAvailable={eyeAvailable} ExpressionAvailable={expressionAvailable}");

        _stateStore = new JsonVirtualTrackerStateStore(_paths.StatePath, _paths.DefaultsPath);
        _state = _stateStore.Load();
        _state.PipeName = _paths.PipeName;
        _state.EyeOutputAllowed = eyeAvailable;
        _state.ExpressionOutputAllowed = expressionAvailable;
        _state.OutputEnabled = true;
        _state.GuiConnected = false;
        _state.GuiLaunchedByModule = false;
        _state.Clamp();

        ModuleInformation.Name = "Virtual Face Tracking";
        NeutralizeOutput();

        _pipeServer = new NamedPipeModuleServer(
            _state.PipeName,
            GetSnapshot,
            HandleEnvelope,
            HandleConnectionChanged);

        var started = _pipeServer.Start();
        VirtualTrackerDiagnostics.Write("Module", $"Pipe server start result={started}");
        if (started && !IsExpectedGuiRunning())
        {
            lock (_stateLock)
            {
                _state.GuiLaunchedByModule = TryLaunchGui(_paths.GuiExePath);
            }
            VirtualTrackerDiagnostics.Write("Module", $"GUI launch attempted. StartedByModule={_state.GuiLaunchedByModule}");
        }

        PersistState();
        _stopwatch = Stopwatch.StartNew();
        _lastUpdate = _stopwatch.Elapsed;
        _lastActiveOutput = null;
        _lastOutputReason = string.Empty;
        return (eyeAvailable && started, expressionAvailable && started);
    }

    public override void Update()
    {
        var now = _stopwatch.Elapsed;
        var elapsed = now - _lastUpdate;
        _lastUpdate = now;

        TrackerRuntimeState snapshot;
        lock (_stateLock)
        {
            snapshot = _state.DeepClone();
        }

        var activeOutput = snapshot.OutputEnabled
                           && snapshot.GuiConnected
                           && (DateTimeOffset.UtcNow - snapshot.LastGuiSeenUtc) <= TimeSpan.FromSeconds(2);
        var outputReason = DescribeOutputState(snapshot, activeOutput);
        if (_lastActiveOutput != activeOutput || !string.Equals(_lastOutputReason, outputReason, StringComparison.Ordinal))
        {
            VirtualTrackerDiagnostics.Write("Module", $"OutputActive={activeOutput} Reason={outputReason}");
            _lastActiveOutput = activeOutput;
            _lastOutputReason = outputReason;
        }

        if (Status != ModuleState.Active || !activeOutput)
        {
            NeutralizeOutput();
            Thread.Sleep(10);
            return;
        }

        var offsets = _simulationEngine.Compute(snapshot, elapsed);
        var mapped = _expressionMapper.Map(snapshot.Manual, offsets, snapshot.AdvancedOverrides);

        if (snapshot.EyeOutputAllowed)
        {
            UnifiedTracking.Data.Eye.Left.Gaze = new Vector2(mapped.Eyes.Left.Yaw, mapped.Eyes.Left.Pitch);
            UnifiedTracking.Data.Eye.Right.Gaze = new Vector2(mapped.Eyes.Right.Yaw, mapped.Eyes.Right.Pitch);
            UnifiedTracking.Data.Eye.Left.Openness = mapped.Eyes.Left.Openness;
            UnifiedTracking.Data.Eye.Right.Openness = mapped.Eyes.Right.Openness;
        }

        if (snapshot.ExpressionOutputAllowed)
        {
            for (var index = 0; index < UnifiedTracking.Data.Shapes.Length; index++)
            {
                UnifiedTracking.Data.Shapes[index].Weight = 0f;
            }

            foreach (var pair in mapped.Shapes)
            {
                if (_shapeLookup.TryGetValue(pair.Key, out var shape))
                {
                    UnifiedTracking.Data.Shapes[(int)shape].Weight = pair.Value;
                }
            }
        }

        Thread.Sleep(10);
    }

    public override void Teardown()
    {
        try
        {
            if (_pipeServer is not null && _state.GuiLaunchedByModule)
            {
                _pipeServer.SendAsync(PipeEnvelope.Create(
                    PipeMessageTypes.Shutdown,
                    new ShutdownMessage
                    {
                        CloseGui = true,
                        Reason = "Module teardown"
                    })).GetAwaiter().GetResult();
            }
        }
        catch
        {
        }

        lock (_stateLock)
        {
            _state.OutputEnabled = false;
            _state.GuiConnected = false;
            _state.GuiLaunchedByModule = false;
        }

        NeutralizeOutput();
        _pipeServer?.Stop();
        PersistState();
    }

    private void HandleEnvelope(PipeEnvelope envelope)
    {
        var shutdownRequested = false;
        lock (_stateLock)
        {
            shutdownRequested = TrackerStateReducer.Apply(_state, envelope);
        }

        if (string.Equals(envelope.MessageType, PipeMessageTypes.Ping, StringComparison.Ordinal))
        {
            var now = DateTimeOffset.UtcNow;
            if ((now - _lastHeartbeatLogUtc) >= TimeSpan.FromSeconds(1))
            {
                VirtualTrackerDiagnostics.Write("Module", "Heartbeat received from GUI");
                _lastHeartbeatLogUtc = now;
            }
        }
        else
        {
            VirtualTrackerDiagnostics.Write(
                "Module",
                $"Applied {envelope.MessageType}. OutputEnabled={_state.OutputEnabled} GuiConnected={_state.GuiConnected} SimulationEnabled={_state.Simulation.Enabled}");
        }

        if (shutdownRequested)
        {
            VirtualTrackerDiagnostics.Write("Module", "Shutdown requested by GUI");
        }

        PersistState();
    }

    private void HandleConnectionChanged(bool connected)
    {
        lock (_stateLock)
        {
            _state.GuiConnected = connected;
            if (connected)
            {
                _state.LastGuiSeenUtc = DateTimeOffset.UtcNow;
            }
        }

        VirtualTrackerDiagnostics.Write("Module", $"ConnectionChanged connected={connected}");
        PersistState();
    }

    private TrackerRuntimeState GetSnapshot()
    {
        lock (_stateLock)
        {
            _state.Clamp();
            return _state.DeepClone();
        }
    }

    private void PersistState()
    {
        try
        {
            lock (_stateLock)
            {
                _stateStore?.Save(_state);
            }
        }
        catch
        {
            VirtualTrackerDiagnostics.Write("Module", "PersistState failed");
        }
    }

    private void NeutralizeOutput()
    {
        UnifiedTracking.Data.Eye.Left.Gaze = new Vector2(0f, 0f);
        UnifiedTracking.Data.Eye.Right.Gaze = new Vector2(0f, 0f);
        UnifiedTracking.Data.Eye.Left.Openness = 1f;
        UnifiedTracking.Data.Eye.Right.Openness = 1f;

        for (var index = 0; index < UnifiedTracking.Data.Shapes.Length; index++)
        {
            UnifiedTracking.Data.Shapes[index].Weight = 0f;
        }
    }

    private bool TryLaunchGui(string guiPath)
    {
        if (!File.Exists(guiPath))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = guiPath,
                WorkingDirectory = Path.GetDirectoryName(guiPath),
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsExpectedGuiRunning()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("VirtualFaceTracking.Gui"))
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, _paths?.GuiExePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private string DescribeOutputState(TrackerRuntimeState snapshot, bool activeOutput)
    {
        if (Status != ModuleState.Active)
        {
            return $"ModuleStatus={Status}";
        }

        if (activeOutput)
        {
            return $"ManualNonZero={HasNonZeroManual(snapshot.Manual)} SimulationEnabled={snapshot.Simulation.Enabled}";
        }

        if (!snapshot.OutputEnabled)
        {
            return "OutputDisabled";
        }

        if (!snapshot.GuiConnected)
        {
            return "GuiDisconnected";
        }

        return "GuiHeartbeatTimedOut";
    }

    private static bool HasNonZeroManual(ManualControlState state) =>
        state.LeftEyeYaw != 0f
        || state.RightEyeYaw != 0f
        || state.LeftEyePitch != 0f
        || state.RightEyePitch != 0f
        || state.LeftEyeBlink != 0f
        || state.RightEyeBlink != 0f
        || state.LeftBrowRaise != 0f
        || state.RightBrowRaise != 0f
        || state.LeftBrowLower != 0f
        || state.RightBrowLower != 0f
        || state.JawOpen != 0f
        || state.JawSideways != 0f
        || state.JawForwardBack != 0f
        || state.MouthOpen != 0f
        || state.Smile != 0f
        || state.Frown != 0f
        || state.LipPucker != 0f
        || state.LipFunnel != 0f
        || state.LipSuck != 0f
        || state.CheekPuffSuck != 0f
        || state.CheekSquint != 0f
        || state.NoseSneer != 0f;
}
