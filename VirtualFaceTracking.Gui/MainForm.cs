using System.Diagnostics;
using VirtualFaceTracking.Gui.Runtime;
using VirtualFaceTracking.Shared;
using VirtualFaceTracking.Shared.Diagnostics;
using VirtualFaceTracking.Shared.IPC;
using VirtualFaceTracking.Shared.Mapping;

namespace VirtualFaceTracking.Gui;

public sealed class MainForm : Form
{
    private sealed class SliderBinding
    {
        public required string Key { get; init; }
        public required TrackBar Bar { get; init; }
        public required Label ValueLabel { get; init; }
        public required float Min { get; init; }
        public required float Max { get; init; }
        public required float ResetValue { get; init; }
        public required Action<float> Setter { get; init; }
        public required Func<float> Getter { get; init; }
    }

    private readonly VirtualTrackerPipeClient _client = new();
    private readonly System.Windows.Forms.Timer _sendTimer = new() { Interval = 33 };
    private readonly System.Windows.Forms.Timer _diagnosticsTimer = new() { Interval = 500 };

    private readonly Dictionary<string, SliderBinding> _sliders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _linkToggles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _overrideToggles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SliderBinding> _overrideSliders = new(StringComparer.Ordinal);

    private readonly ManualControlState _manual = new();
    private readonly SimulationState _simulation = new();
    private readonly AdvancedOverrideState _advanced = AdvancedOverrideState.CreateDefault(VirtualExpressionCatalog.AllShapeNames);
    private readonly GuiSessionState _gui = new();

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Label _statusLabel = new() { AutoSize = true, Padding = new Padding(8, 10, 0, 0) };
    private readonly TextBox _diagnosticsConsole = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        BackColor = Color.FromArgb(18, 18, 18),
        ForeColor = Color.Gainsboro,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };
    private readonly TextBox _diagnosticsPathBox = new()
    {
        ReadOnly = true,
        Width = 520
    };
    private readonly CheckBox _verboseGuiPipeLoggingCheckBox = new()
    {
        Text = "Verbose GuiPipe Logging",
        AutoSize = true,
        Margin = new Padding(3, 8, 12, 3)
    };

    private bool _manualDirty;
    private bool _simulationDirty;
    private bool _advancedDirty;
    private bool _suppressEvents;
    private bool _layoutApplied;
    private bool _closeRequestedByModule;
    private bool _connected;
    private bool _flushInProgress;
    private bool _awaitingInitialHeartbeat = true;
    private DateTimeOffset _lastPingSentUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastHeartbeatFailureLogUtc = DateTimeOffset.MinValue;
    private long _diagnosticsLastLength = -1;
    private DateTime _diagnosticsLastWriteUtc = DateTime.MinValue;

    public MainForm()
    {
        Text = "Virtual Face Tracking";
        MinimumSize = new Size(960, 720);
        StartPosition = FormStartPosition.CenterScreen;
        VirtualTrackerDiagnostics.Configure(AppContext.BaseDirectory);
        VirtualTrackerDiagnostics.Write("Gui", "GUI starting");
        _client.VerboseLoggingEnabled = _gui.VerboseGuiPipeLogging;

        BuildLayout();
        _diagnosticsPathBox.Text = VirtualTrackerDiagnostics.LogPath;
        RefreshDiagnostics(force: true);

        _client.SnapshotReceived += snapshot => BeginInvoke(() => ApplySnapshot(snapshot.State));
        _client.ShutdownRequested += shutdown =>
        {
            if (!shutdown.CloseGui)
            {
                return;
            }

            _closeRequestedByModule = true;
            BeginInvoke(Close);
        };
        _client.ConnectionChanged += connected => BeginInvoke(() =>
        {
            _connected = connected;
            if (connected)
            {
                _lastPingSentUtc = DateTimeOffset.MinValue;
                _awaitingInitialHeartbeat = true;
                _manualDirty = true;
                _simulationDirty = true;
                _advancedDirty = true;
                VirtualTrackerDiagnostics.Write("Gui", "Pipe connected; scheduling heartbeat and full state sync");
                _ = FlushPendingAsync();
            }
            else
            {
                _awaitingInitialHeartbeat = true;
            }

            UpdateStatusText();
        });

        _sendTimer.Tick += async (_, _) => await FlushPendingAsync();
        _diagnosticsTimer.Tick += (_, _) => RefreshDiagnostics();
        _sendTimer.Start();
        _diagnosticsTimer.Start();
        _client.Start(PipeProtocol.DefaultPipeName);
        UpdateStatusText();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _sendTimer.Stop();
        _diagnosticsTimer.Stop();
        _client.Dispose();
        base.OnFormClosing(e);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(12),
            WrapContents = true
        };

        commands.Controls.Add(MakeButton("Enable Output", async () =>
        {
            await SetOutputEnabledAsync(true);
        }));
        commands.Controls.Add(MakeButton("Disable Output", async () =>
        {
            await SetOutputEnabledAsync(false);
        }));
        commands.Controls.Add(MakeButton("Reset Eyes", async () => await SendResetAsync(ResetSection.Eyes)));
        commands.Controls.Add(MakeButton("Reset Brows", async () => await SendResetAsync(ResetSection.Brows)));
        commands.Controls.Add(MakeButton("Reset Face", async () => await SendResetAsync(ResetSection.Face)));
        commands.Controls.Add(MakeButton("Reset All", async () => await SendResetAsync(ResetSection.All)));
        commands.Controls.Add(_statusLabel);

        _tabs.TabPages.Add(BuildEyesTab());
        _tabs.TabPages.Add(BuildBrowsTab());
        _tabs.TabPages.Add(BuildFaceTab());
        _tabs.TabPages.Add(BuildSimulationTab());
        _tabs.TabPages.Add(BuildDiagnosticsTab());
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressEvents)
            {
                return;
            }

            _gui.SelectedPanel = GetTrackerPanelForTabIndex(_tabs.SelectedIndex);
            MarkManualDirty();
        };

        root.Controls.Add(commands, 0, 0);
        root.Controls.Add(_tabs, 0, 1);

        Controls.Add(root);
    }

    private TabPage BuildDiagnosticsTab()
    {
        var page = new TabPage("Diagnostics");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(12),
            WrapContents = true
        };

        toolbar.Controls.Add(MakeButton("Refresh Log", () =>
        {
            RefreshDiagnostics(force: true);
            return Task.CompletedTask;
        }));
        toolbar.Controls.Add(MakeButton("Clear Log", () =>
        {
            VirtualTrackerDiagnostics.Clear();
            VirtualTrackerDiagnostics.Write("Gui", "Diagnostics log cleared");
            RefreshDiagnostics(force: true);
            return Task.CompletedTask;
        }));
        toolbar.Controls.Add(MakeButton("Open Folder", () =>
        {
            var folder = Path.GetDirectoryName(VirtualTrackerDiagnostics.LogPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }

            return Task.CompletedTask;
        }));
        _verboseGuiPipeLoggingCheckBox.Checked = _gui.VerboseGuiPipeLogging;
        _verboseGuiPipeLoggingCheckBox.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents)
            {
                return;
            }

            _gui.VerboseGuiPipeLogging = _verboseGuiPipeLoggingCheckBox.Checked;
            _client.VerboseLoggingEnabled = _gui.VerboseGuiPipeLogging;
            VirtualTrackerDiagnostics.Write("Gui", $"Verbose GuiPipe logging set to {_gui.VerboseGuiPipeLogging}");
            MarkManualDirty();
        };
        toolbar.Controls.Add(_verboseGuiPipeLoggingCheckBox);
        toolbar.Controls.Add(_diagnosticsPathBox);

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_diagnosticsConsole, 0, 1);
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildEyesTab()
    {
        var page = new TabPage("Eyes");
        var panel = CreateScrollablePanel();
        var table = CreateTable();

        AddLinkToggle(table, "Link Eye Yaw", () => _manual.LinkEyeYaw, value => _manual.LinkEyeYaw = value);
        AddSlider(table, "Left Eye Yaw", -1f, 1f, () => _manual.LeftEyeYaw, value => _manual.LeftEyeYaw = value);
        AddSlider(table, "Right Eye Yaw", -1f, 1f, () => _manual.RightEyeYaw, value => _manual.RightEyeYaw = value);

        AddLinkToggle(table, "Link Eye Pitch", () => _manual.LinkEyePitch, value => _manual.LinkEyePitch = value);
        AddSlider(table, "Left Eye Pitch", -1f, 1f, () => _manual.LeftEyePitch, value => _manual.LeftEyePitch = value);
        AddSlider(table, "Right Eye Pitch", -1f, 1f, () => _manual.RightEyePitch, value => _manual.RightEyePitch = value);

        AddLinkToggle(table, "Link Blink", () => _manual.LinkEyeBlink, value => _manual.LinkEyeBlink = value);
        AddSlider(table, "Left Blink", 0f, 1f, () => _manual.LeftEyeBlink, value => _manual.LeftEyeBlink = value);
        AddSlider(table, "Right Blink", 0f, 1f, () => _manual.RightEyeBlink, value => _manual.RightEyeBlink = value);

        panel.Controls.Add(table);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildBrowsTab()
    {
        var page = new TabPage("Brows");
        var panel = CreateScrollablePanel();
        var table = CreateTable();

        AddLinkToggle(table, "Link Brow Raise", () => _manual.LinkBrowRaise, value => _manual.LinkBrowRaise = value);
        AddSlider(table, "Left Brow Raise", 0f, 1f, () => _manual.LeftBrowRaise, value => _manual.LeftBrowRaise = value);
        AddSlider(table, "Right Brow Raise", 0f, 1f, () => _manual.RightBrowRaise, value => _manual.RightBrowRaise = value);

        AddLinkToggle(table, "Link Brow Lower", () => _manual.LinkBrowLower, value => _manual.LinkBrowLower = value);
        AddSlider(table, "Left Brow Lower", 0f, 1f, () => _manual.LeftBrowLower, value => _manual.LeftBrowLower = value);
        AddSlider(table, "Right Brow Lower", 0f, 1f, () => _manual.RightBrowLower, value => _manual.RightBrowLower = value);

        panel.Controls.Add(table);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildFaceTab()
    {
        var page = new TabPage("Face");
        var panel = CreateScrollablePanel();
        var table = CreateTable();

        AddSlider(table, "Jaw Open", 0f, 1f, () => _manual.JawOpen, value => _manual.JawOpen = value);
        AddSlider(table, "Jaw Sideways", -1f, 1f, () => _manual.JawSideways, value => _manual.JawSideways = value);
        AddSlider(table, "Jaw Forward/Back", -1f, 1f, () => _manual.JawForwardBack, value => _manual.JawForwardBack = value);
        AddSlider(table, "Mouth Open", 0f, 1f, () => _manual.MouthOpen, value => _manual.MouthOpen = value);
        AddSlider(table, "Smile", 0f, 1f, () => _manual.Smile, value => _manual.Smile = value);
        AddSlider(table, "Frown", 0f, 1f, () => _manual.Frown, value => _manual.Frown = value);
        AddSlider(table, "Lip Pucker", 0f, 1f, () => _manual.LipPucker, value => _manual.LipPucker = value);
        AddSlider(table, "Lip Funnel", 0f, 1f, () => _manual.LipFunnel, value => _manual.LipFunnel = value);
        AddSlider(table, "Lip Suck", 0f, 1f, () => _manual.LipSuck, value => _manual.LipSuck = value);
        AddSlider(table, "Cheek Puff/Suck", -1f, 1f, () => _manual.CheekPuffSuck, value => _manual.CheekPuffSuck = value);
        AddSlider(table, "Cheek Squint", 0f, 1f, () => _manual.CheekSquint, value => _manual.CheekSquint = value);
        AddSlider(table, "Nose Sneer", 0f, 1f, () => _manual.NoseSneer, value => _manual.NoseSneer = value);

        panel.Controls.Add(table);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildSimulationTab()
    {
        var page = new TabPage("Simulation");
        var panel = CreateScrollablePanel();
        var table = CreateTable();

        AddBoolRow(table, "Simulation Enabled", () => _simulation.Enabled, value => _simulation.Enabled = value, () => _simulationDirty = true);
        AddSlider(table, "Intensity", 0f, 1f, () => _simulation.Intensity, value =>
        {
            _simulation.Intensity = value;
            _simulationDirty = true;
        }, isSimulation: true, resetValue: 0.35f);
        AddSlider(table, "Speed", 0f, 1f, () => _simulation.Speed, value =>
        {
            _simulation.Speed = value;
            _simulationDirty = true;
        }, isSimulation: true, resetValue: 0.35f);
        AddBoolRow(table, "Simulate Eyes", () => _simulation.SimulateEyes, value => _simulation.SimulateEyes = value, () => _simulationDirty = true);
        AddBoolRow(table, "Simulate Blink", () => _simulation.SimulateBlink, value => _simulation.SimulateBlink = value, () => _simulationDirty = true);
        AddBoolRow(table, "Simulate Brows", () => _simulation.SimulateBrows, value => _simulation.SimulateBrows = value, () => _simulationDirty = true);
        AddBoolRow(table, "Simulate Face", () => _simulation.SimulateFace, value => _simulation.SimulateFace = value, () => _simulationDirty = true);

        panel.Controls.Add(table);
        page.Controls.Add(panel);
        return page;
    }

    private async Task FlushPendingAsync()
    {
        if (_flushInProgress)
        {
            return;
        }

        _flushInProgress = true;
        try
        {
            if (!_connected)
            {
                return;
            }

            if ((DateTimeOffset.UtcNow - _lastPingSentUtc) > TimeSpan.FromMilliseconds(250))
            {
                _lastPingSentUtc = DateTimeOffset.UtcNow;
                var sent = await _client.SendAsync(PipeEnvelope.Create(PipeMessageTypes.Ping, new PingMessage()));
                if (sent)
                {
                    if (_awaitingInitialHeartbeat)
                    {
                        VirtualTrackerDiagnostics.Write("Gui", "Initial heartbeat sent");
                        _awaitingInitialHeartbeat = false;
                    }
                }
                else if ((DateTimeOffset.UtcNow - _lastHeartbeatFailureLogUtc) >= TimeSpan.FromSeconds(1))
                {
                    VirtualTrackerDiagnostics.Write("Gui", "Heartbeat send failed");
                    _lastHeartbeatFailureLogUtc = DateTimeOffset.UtcNow;
                }
            }

            if (_manualDirty)
            {
                _manual.Clamp();
                var sent = await _client.SendAsync(PipeEnvelope.Create(
                    PipeMessageTypes.PatchManualState,
                    new PatchManualStateMessage
                    {
                        Manual = _manual.DeepClone(),
                        Gui = _gui.DeepClone()
                    }));
                if (sent)
                {
                    VirtualTrackerDiagnostics.Write("Gui", "Manual state sent successfully");
                    _manualDirty = false;
                }
            }

            if (_simulationDirty)
            {
                _simulation.Clamp();
                var sent = await _client.SendAsync(PipeEnvelope.Create(
                    PipeMessageTypes.PatchSimulationState,
                    new PatchSimulationStateMessage { Simulation = _simulation.DeepClone() }));
                if (sent)
                {
                    VirtualTrackerDiagnostics.Write("Gui", $"Simulation state sent successfully. Enabled={_simulation.Enabled} Intensity={_simulation.Intensity:0.000} Speed={_simulation.Speed:0.000}");
                    _simulationDirty = false;
                }
            }

            if (_advancedDirty)
            {
                _advanced.Clamp();
                var sent = await _client.SendAsync(PipeEnvelope.Create(
                    PipeMessageTypes.PatchAdvancedOverrides,
                    new PatchAdvancedOverridesMessage { AdvancedOverrides = _advanced.DeepClone() }));
                if (sent)
                {
                    VirtualTrackerDiagnostics.Write("Gui", "Advanced overrides sent successfully");
                    _advancedDirty = false;
                }
            }
        }
        finally
        {
            _flushInProgress = false;
        }
    }

    private async Task SendResetAsync(ResetSection section)
    {
        VirtualTrackerDiagnostics.Write("Gui", $"Reset requested for {section}");
        await _client.SendAsync(PipeEnvelope.Create(PipeMessageTypes.ResetSection, new ResetSectionMessage { Section = section }));
    }

    private Task SetOutputEnabledAsync(bool enabled)
    {
        VirtualTrackerDiagnostics.Write("Gui", $"SetOutputEnabled requested value={enabled}");
        return _client.SendAsync(PipeEnvelope.Create(
            PipeMessageTypes.SetOutputEnabled,
            new SetOutputEnabledMessage { Enabled = enabled }));
    }

    private void ApplySnapshot(TrackerRuntimeState state)
    {
        _suppressEvents = true;
        try
        {
            if (!_manualDirty)
            {
                _manual.LinkEyeYaw = state.Manual.LinkEyeYaw;
                _manual.LinkEyePitch = state.Manual.LinkEyePitch;
                _manual.LinkEyeBlink = state.Manual.LinkEyeBlink;
                _manual.LinkBrowRaise = state.Manual.LinkBrowRaise;
                _manual.LinkBrowLower = state.Manual.LinkBrowLower;
                _manual.LeftEyeYaw = state.Manual.LeftEyeYaw;
                _manual.RightEyeYaw = state.Manual.RightEyeYaw;
                _manual.LeftEyePitch = state.Manual.LeftEyePitch;
                _manual.RightEyePitch = state.Manual.RightEyePitch;
                _manual.LeftEyeBlink = state.Manual.LeftEyeBlink;
                _manual.RightEyeBlink = state.Manual.RightEyeBlink;
                _manual.LeftBrowRaise = state.Manual.LeftBrowRaise;
                _manual.RightBrowRaise = state.Manual.RightBrowRaise;
                _manual.LeftBrowLower = state.Manual.LeftBrowLower;
                _manual.RightBrowLower = state.Manual.RightBrowLower;
                _manual.JawOpen = state.Manual.JawOpen;
                _manual.JawSideways = state.Manual.JawSideways;
                _manual.JawForwardBack = state.Manual.JawForwardBack;
                _manual.MouthOpen = state.Manual.MouthOpen;
                _manual.Smile = state.Manual.Smile;
                _manual.Frown = state.Manual.Frown;
                _manual.LipPucker = state.Manual.LipPucker;
                _manual.LipFunnel = state.Manual.LipFunnel;
                _manual.LipSuck = state.Manual.LipSuck;
                _manual.CheekPuffSuck = state.Manual.CheekPuffSuck;
                _manual.CheekSquint = state.Manual.CheekSquint;
                _manual.NoseSneer = state.Manual.NoseSneer;
                _manual.Clamp();
            }

            if (!_simulationDirty)
            {
                _simulation.Enabled = state.Simulation.Enabled;
                _simulation.Intensity = state.Simulation.Intensity;
                _simulation.Speed = state.Simulation.Speed;
                _simulation.SimulateEyes = state.Simulation.SimulateEyes;
                _simulation.SimulateBlink = state.Simulation.SimulateBlink;
                _simulation.SimulateBrows = state.Simulation.SimulateBrows;
                _simulation.SimulateFace = state.Simulation.SimulateFace;
            }

            if (!_advancedDirty)
            {
                _advanced.Shapes.Clear();
                foreach (var entry in state.AdvancedOverrides.Shapes)
                {
                    _advanced.Shapes[entry.Key] = entry.Value.DeepClone();
                }
                _advanced.EnsureCatalog(VirtualExpressionCatalog.AllShapeNames);
            }

            _gui.Left = state.Gui.Left;
            _gui.Top = state.Gui.Top;
            _gui.Width = state.Gui.Width;
            _gui.Height = state.Gui.Height;
            _gui.Maximized = state.Gui.Maximized;
            _gui.SelectedPanel = state.Gui.SelectedPanel;
            _gui.VerboseGuiPipeLogging = state.Gui.VerboseGuiPipeLogging;
            _client.VerboseLoggingEnabled = _gui.VerboseGuiPipeLogging;

            if (!_layoutApplied)
            {
                ApplyWindowLayout();
                _layoutApplied = true;
            }

            RefreshControlValues();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void RefreshControlValues()
    {
        foreach (var pair in _linkToggles)
        {
            pair.Value.Checked = pair.Key switch
            {
                "LinkEyeYaw" => _manual.LinkEyeYaw,
                "LinkEyePitch" => _manual.LinkEyePitch,
                "LinkEyeBlink" => _manual.LinkEyeBlink,
                "LinkBrowRaise" => _manual.LinkBrowRaise,
                "LinkBrowLower" => _manual.LinkBrowLower,
                _ => pair.Value.Checked
            };
        }

        foreach (var slider in _sliders.Values)
        {
            slider.Bar.Value = FloatToTrack(slider.Getter(), slider.Min, slider.Max);
            slider.ValueLabel.Text = slider.Getter().ToString("0.000");
        }

        foreach (var pair in _overrideToggles)
        {
            pair.Value.Checked = _advanced.Shapes[pair.Key].UseOverride;
        }

        foreach (var slider in _overrideSliders.Values)
        {
            slider.Bar.Value = FloatToTrack(slider.Getter(), slider.Min, slider.Max);
            slider.ValueLabel.Text = slider.Getter().ToString("0.000");
        }

        var selectedIndex = GetTabIndexForTrackerPanel(_gui.SelectedPanel);
        if (_tabs.SelectedIndex != selectedIndex)
        {
            _tabs.SelectedIndex = selectedIndex;
        }
        _verboseGuiPipeLoggingCheckBox.Checked = _gui.VerboseGuiPipeLogging;
        UpdateStatusText();
    }

    private TrackerPanel GetTrackerPanelForTabIndex(int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= _tabs.TabPages.Count)
        {
            return TrackerPanel.Eyes;
        }

        return _tabs.TabPages[selectedIndex].Text switch
        {
            "Eyes" => TrackerPanel.Eyes,
            "Brows" => TrackerPanel.Brows,
            "Face" => TrackerPanel.Face,
            "Simulation" => TrackerPanel.Simulation,
            "Diagnostics" => TrackerPanel.Diagnostics,
            _ => TrackerPanel.Eyes
        };
    }

    private int GetTabIndexForTrackerPanel(TrackerPanel panel)
    {
        var title = panel switch
        {
            TrackerPanel.Eyes => "Eyes",
            TrackerPanel.Brows => "Brows",
            TrackerPanel.Face => "Face",
            TrackerPanel.Simulation => "Simulation",
            TrackerPanel.Diagnostics => "Diagnostics",
            _ => "Eyes"
        };

        var index = _tabs.TabPages.IndexOfKey(title);
        if (index >= 0)
        {
            return index;
        }

        for (var i = 0; i < _tabs.TabPages.Count; i++)
        {
            if (string.Equals(_tabs.TabPages[i].Text, title, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    private void ApplyWindowLayout()
    {
        if (_gui.Width > 0 && _gui.Height > 0)
        {
            Size = new Size(_gui.Width, _gui.Height);
        }

        if (_gui.Left.HasValue && _gui.Top.HasValue)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(_gui.Left.Value, _gui.Top.Value);
        }

        WindowState = _gui.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);
        CaptureWindowLayout();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        CaptureWindowLayout();
    }

    private void CaptureWindowLayout()
    {
        if (_suppressEvents)
        {
            return;
        }

        if (WindowState == FormWindowState.Normal)
        {
            _gui.Left = Left;
            _gui.Top = Top;
            _gui.Width = Width;
            _gui.Height = Height;
        }

        _gui.Maximized = WindowState == FormWindowState.Maximized;
        MarkManualDirty();
    }

    private void AddLinkToggle(TableLayoutPanel table, string label, Func<bool> getter, Action<bool> setter)
    {
        AddBoolRow(table, label, getter, setter, () =>
        {
            MarkManualDirty();
            _manual.Clamp();
            RefreshControlValues();
        });
    }

    private void AddBoolRow(TableLayoutPanel table, string label, Func<bool> getter, Action<bool> setter, Action afterChange)
    {
        var title = new Label { Text = label, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        var check = new CheckBox { Checked = getter(), AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
        check.CheckedChanged += (_, _) =>
        {
            if (_suppressEvents)
            {
                return;
            }

            setter(check.Checked);
            afterChange();
        };

        table.Controls.Add(title);
        table.Controls.Add(check);
        _linkToggles[label.Replace(" ", string.Empty)] = check;
    }

    private void AddSlider(
        TableLayoutPanel table,
        string label,
        float min,
        float max,
        Func<float> getter,
        Action<float> setter,
        bool isSimulation = false,
        float? resetValue = null)
    {
        var title = new Label { Text = label, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var bar = new TrackBar
        {
            Minimum = 0,
            Maximum = 1000,
            TickFrequency = 100,
            Width = 420
        };
        var valueLabel = new Label { AutoSize = true, Width = 48, TextAlign = ContentAlignment.MiddleRight };

        bar.Scroll += (_, _) =>
        {
            if (_suppressEvents)
            {
                return;
            }

            var value = TrackToFloat(bar.Value, min, max);
            setter(value);
            if (isSimulation)
            {
                _simulationDirty = true;
            }
            else
            {
                _manual.Clamp();
                MarkManualDirty();
            }

            RefreshControlValues();
        };
        bar.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || _suppressEvents)
            {
                return;
            }

            var targetValue = resetValue ?? GetDefaultResetValue(min, max);
            setter(targetValue);
            if (isSimulation)
            {
                _simulationDirty = true;
            }
            else
            {
                _manual.Clamp();
                MarkManualDirty();
            }

            RefreshControlValues();
        };

        panel.Controls.Add(bar);
        panel.Controls.Add(valueLabel);

        table.Controls.Add(title);
        table.Controls.Add(panel);

        _sliders[label] = new SliderBinding
        {
            Key = label,
            Bar = bar,
            ValueLabel = valueLabel,
            Min = min,
            Max = max,
            ResetValue = resetValue ?? GetDefaultResetValue(min, max),
            Setter = setter,
            Getter = getter
        };
    }

    private void MarkManualDirty()
    {
        _manualDirty = true;
    }

    private void UpdateStatusText()
    {
        _statusLabel.Text = _closeRequestedByModule
            ? "Closing on module request"
            : _connected
                ? "Pipe connected"
                : "Waiting for module";
    }

    private void RefreshDiagnostics(bool force = false)
    {
        try
        {
            var path = VirtualTrackerDiagnostics.LogPath;
            var info = new FileInfo(path);
            if (!force)
            {
                if (info.Exists && info.Length == _diagnosticsLastLength && info.LastWriteTimeUtc == _diagnosticsLastWriteUtc)
                {
                    return;
                }

                if (!info.Exists && _diagnosticsLastLength == 0 && _diagnosticsLastWriteUtc == DateTime.MinValue)
                {
                    return;
                }
            }

            var content = VirtualTrackerDiagnostics.ReadAll();
            if (content.Length > 48000)
            {
                content = content[^48000..];
            }

            _diagnosticsConsole.Text = content;
            _diagnosticsConsole.SelectionStart = _diagnosticsConsole.TextLength;
            _diagnosticsConsole.ScrollToCaret();

            _diagnosticsLastLength = info.Exists ? info.Length : 0;
            _diagnosticsLastWriteUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
        }
        catch
        {
        }
    }

    private static FlowLayoutPanel CreateScrollablePanel() => new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        Padding = new Padding(12),
        WrapContents = false,
        FlowDirection = FlowDirection.TopDown
    };

    private static TableLayoutPanel CreateTable() => new()
    {
        ColumnCount = 2,
        AutoSize = true,
        Dock = DockStyle.Top
    };

    private static Button MakeButton(string text, Func<Task> action)
    {
        var button = new Button { Text = text, AutoSize = true, Margin = new Padding(3, 3, 8, 3) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static int FloatToTrack(float value, float min, float max)
    {
        var normalized = (value - min) / (max - min);
        return (int)Math.Round(Math.Clamp(normalized, 0f, 1f) * 1000f);
    }

    private static float TrackToFloat(int value, float min, float max)
    {
        var normalized = value / 1000f;
        return min + ((max - min) * normalized);
    }

    private void InitializeComponent()
    {
        SuspendLayout();
        // 
        // MainForm
        // 
        ClientSize = new Size(1018, 528);
        Name = "MainForm";
        ResumeLayout(false);

    }

    private static float GetDefaultResetValue(float min, float max)
    {
        if (min <= 0f && max >= 0f)
        {
            return 0f;
        }

        return min;
    }
}
