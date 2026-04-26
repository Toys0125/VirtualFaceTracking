using VirtualFaceTracking.Shared;
using VirtualFaceTracking.Shared.IPC;
using VirtualFaceTracking.Shared.Mapping;
using VirtualFaceTracking.Shared.Persistence;
using VirtualFaceTracking.Shared.Simulation;

namespace VirtualFaceTracking.Tests;

public sealed class VirtualTrackerTests
{
    private readonly VirtualExpressionMapper _mapper = new();

    [Fact]
    public void LinkedControlsClampAndMirror()
    {
        var manual = new ManualControlState
        {
            LinkEyeYaw = true,
            LeftEyeYaw = 1.5f,
            RightEyeYaw = -0.2f,
            LinkBrowRaise = true,
            LeftBrowRaise = 0.75f,
            RightBrowRaise = 0.1f
        };

        manual.Clamp();

        Assert.Equal(1f, manual.LeftEyeYaw);
        Assert.Equal(1f, manual.RightEyeYaw);
        Assert.Equal(0.75f, manual.RightBrowRaise);
    }

    [Fact]
    public void UnlinkedControlsStayIndependent()
    {
        var manual = new ManualControlState
        {
            LinkEyeYaw = false,
            LeftEyeYaw = -0.25f,
            RightEyeYaw = 0.55f
        };

        manual.Clamp();

        Assert.Equal(-0.25f, manual.LeftEyeYaw);
        Assert.Equal(0.55f, manual.RightEyeYaw);
    }

    [Fact]
    public void BlinkMapsToOpenness()
    {
        var manual = new ManualControlState
        {
            LeftEyeBlink = 0.3f,
            RightEyeBlink = 0.8f,
            LinkEyeBlink = false
        };

        var mapped = _mapper.Map(manual, new SimulationOffsets(), AdvancedOverrideState.CreateDefault(VirtualExpressionCatalog.AllShapeNames));

        Assert.Equal(0.7f, mapped.Eyes.Left.Openness, 3);
        Assert.Equal(0.2f, mapped.Eyes.Right.Openness, 3);
    }

    [Fact]
    public void SignedJawAndCheekRoutingAreMutuallyExclusive()
    {
        var manual = new ManualControlState
        {
            JawSideways = -0.6f,
            JawForwardBack = 0.4f,
            CheekPuffSuck = -0.5f
        };

        var mapped = _mapper.Map(manual, new SimulationOffsets(), AdvancedOverrideState.CreateDefault(VirtualExpressionCatalog.AllShapeNames));

        Assert.Equal(0.6f, mapped.Shapes["JawLeft"], 3);
        Assert.Equal(0f, mapped.Shapes["JawRight"]);
        Assert.Equal(0.4f, mapped.Shapes["JawForward"], 3);
        Assert.Equal(0f, mapped.Shapes["JawBackward"]);
        Assert.Equal(0.5f, mapped.Shapes["CheekSuckLeft"], 3);
        Assert.Equal(0f, mapped.Shapes["CheekPuffLeft"]);
    }

    [Fact]
    public void AdvancedOverridesWinOverComputedValues()
    {
        var overrides = AdvancedOverrideState.CreateDefault(VirtualExpressionCatalog.AllShapeNames);
        overrides.Shapes["MouthCornerPullLeft"].UseOverride = true;
        overrides.Shapes["MouthCornerPullLeft"].Value = 0.9f;

        var mapped = _mapper.Map(
            new ManualControlState { Smile = 0.2f },
            new SimulationOffsets { Smile = 0.1f },
            overrides);

        Assert.Equal(0.9f, mapped.Shapes["MouthCornerPullLeft"], 3);
    }

    [Fact]
    public void SimulationRespectsBoundsAndStaysSmooth()
    {
        var engine = new VirtualSimulationEngine(seed: 42);
        var state = new TrackerRuntimeState
        {
            Simulation = new SimulationState
            {
                Enabled = true,
                Intensity = 1f,
                Speed = 1f
            }
        };

        var previous = engine.Compute(state, TimeSpan.FromMilliseconds(16));
        for (var i = 0; i < 180; i++)
        {
            var current = engine.Compute(state, TimeSpan.FromMilliseconds(16));
            Assert.InRange(current.LeftEyeYaw, -1f, 1f);
            Assert.InRange(current.RightEyePitch, -1f, 1f);
            Assert.InRange(current.LeftEyeBlink, 0f, 1f);
            Assert.InRange(current.JawOpen, 0f, 1f);
            Assert.InRange(current.CheekPuffSuck, -1f, 1f);

            Assert.True(Math.Abs(current.LeftEyeYaw - previous.LeftEyeYaw) < 0.45f);
            Assert.True(Math.Abs(current.LeftEyeBlink - previous.LeftEyeBlink) < 0.75f);
            previous = current;
        }
    }

    [Fact]
    public void PingUpdatesGuiHeartbeat()
    {
        var state = new TrackerRuntimeState
        {
            GuiConnected = false,
            LastGuiSeenUtc = DateTimeOffset.MinValue
        };

        TrackerStateReducer.Apply(state, PipeEnvelope.Create(PipeMessageTypes.Ping, new PingMessage()));

        Assert.True(state.GuiConnected);
        Assert.NotEqual(DateTimeOffset.MinValue, state.LastGuiSeenUtc);
    }

    [Fact]
    public void SetOutputEnabledPatchControlsOutputGate()
    {
        var state = new TrackerRuntimeState
        {
            OutputEnabled = false
        };

        TrackerStateReducer.Apply(state, PipeEnvelope.Create(
            PipeMessageTypes.SetOutputEnabled,
            new SetOutputEnabledMessage { Enabled = true }));
        Assert.True(state.OutputEnabled);

        TrackerStateReducer.Apply(state, PipeEnvelope.Create(
            PipeMessageTypes.SetOutputEnabled,
            new SetOutputEnabledMessage { Enabled = false }));
        Assert.False(state.OutputEnabled);
    }

    [Fact]
    public void PersistenceRestoresManualStateButDoesNotAutoEnableOutput()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var defaultsPath = Path.Combine(tempRoot, "defaults.json");
        var statePath = Path.Combine(tempRoot, "state.json");

        try
        {
            var store = new JsonVirtualTrackerStateStore(statePath, defaultsPath);
            var state = new TrackerRuntimeState
            {
                OutputEnabled = true,
                Manual = new ManualControlState { JawOpen = 0.66f }
            };

            store.Save(state);
            var loaded = store.Load();

            Assert.False(loaded.OutputEnabled);
            Assert.Equal(0.66f, loaded.Manual.JawOpen, 3);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
