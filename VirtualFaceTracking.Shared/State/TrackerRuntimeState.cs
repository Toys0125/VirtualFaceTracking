namespace VirtualFaceTracking.Shared;

public enum TrackerPanel
{
    Eyes,
    Brows,
    Face,
    Simulation,
    Diagnostics
}

public enum ResetSection
{
    Eyes,
    Brows,
    Face,
    Simulation,
    All
}

public sealed class GuiSessionState
{
    public int? Left { get; set; }
    public int? Top { get; set; }
    public int Width { get; set; } = 1180;
    public int Height { get; set; } = 860;
    public bool Maximized { get; set; }
    public TrackerPanel SelectedPanel { get; set; } = TrackerPanel.Eyes;
    public bool VerboseGuiPipeLogging { get; set; }

    public GuiSessionState DeepClone() => new()
    {
        Left = Left,
        Top = Top,
        Width = Width,
        Height = Height,
        Maximized = Maximized,
        SelectedPanel = SelectedPanel,
        VerboseGuiPipeLogging = VerboseGuiPipeLogging
    };
}

public sealed class ManualControlState
{
    public bool LinkEyeYaw { get; set; } = true;
    public bool LinkEyePitch { get; set; } = true;
    public bool LinkEyeBlink { get; set; } = true;
    public bool LinkBrowRaise { get; set; } = true;
    public bool LinkBrowLower { get; set; } = true;

    public float LeftEyeYaw { get; set; }
    public float RightEyeYaw { get; set; }
    public float LeftEyePitch { get; set; }
    public float RightEyePitch { get; set; }
    public float LeftEyeBlink { get; set; }
    public float RightEyeBlink { get; set; }

    public float LeftBrowRaise { get; set; }
    public float RightBrowRaise { get; set; }
    public float LeftBrowLower { get; set; }
    public float RightBrowLower { get; set; }

    public float JawOpen { get; set; }
    public float JawSideways { get; set; }
    public float JawForwardBack { get; set; }
    public float MouthOpen { get; set; }
    public float Smile { get; set; }
    public float Frown { get; set; }
    public float LipPucker { get; set; }
    public float LipFunnel { get; set; }
    public float LipSuck { get; set; }
    public float CheekPuffSuck { get; set; }
    public float CheekSquint { get; set; }
    public float NoseSneer { get; set; }

    public ManualControlState DeepClone() => new()
    {
        LinkEyeYaw = LinkEyeYaw,
        LinkEyePitch = LinkEyePitch,
        LinkEyeBlink = LinkEyeBlink,
        LinkBrowRaise = LinkBrowRaise,
        LinkBrowLower = LinkBrowLower,
        LeftEyeYaw = LeftEyeYaw,
        RightEyeYaw = RightEyeYaw,
        LeftEyePitch = LeftEyePitch,
        RightEyePitch = RightEyePitch,
        LeftEyeBlink = LeftEyeBlink,
        RightEyeBlink = RightEyeBlink,
        LeftBrowRaise = LeftBrowRaise,
        RightBrowRaise = RightBrowRaise,
        LeftBrowLower = LeftBrowLower,
        RightBrowLower = RightBrowLower,
        JawOpen = JawOpen,
        JawSideways = JawSideways,
        JawForwardBack = JawForwardBack,
        MouthOpen = MouthOpen,
        Smile = Smile,
        Frown = Frown,
        LipPucker = LipPucker,
        LipFunnel = LipFunnel,
        LipSuck = LipSuck,
        CheekPuffSuck = CheekPuffSuck,
        CheekSquint = CheekSquint,
        NoseSneer = NoseSneer
    };

    public void Clamp()
    {
        LeftEyeYaw = ControlFrame.ClampSigned(LeftEyeYaw);
        RightEyeYaw = ControlFrame.ClampSigned(RightEyeYaw);
        LeftEyePitch = ControlFrame.ClampSigned(LeftEyePitch);
        RightEyePitch = ControlFrame.ClampSigned(RightEyePitch);
        LeftEyeBlink = ControlFrame.ClampScalar(LeftEyeBlink);
        RightEyeBlink = ControlFrame.ClampScalar(RightEyeBlink);

        LeftBrowRaise = ControlFrame.ClampScalar(LeftBrowRaise);
        RightBrowRaise = ControlFrame.ClampScalar(RightBrowRaise);
        LeftBrowLower = ControlFrame.ClampScalar(LeftBrowLower);
        RightBrowLower = ControlFrame.ClampScalar(RightBrowLower);

        JawOpen = ControlFrame.ClampScalar(JawOpen);
        JawSideways = ControlFrame.ClampSigned(JawSideways);
        JawForwardBack = ControlFrame.ClampSigned(JawForwardBack);
        MouthOpen = ControlFrame.ClampScalar(MouthOpen);
        Smile = ControlFrame.ClampScalar(Smile);
        Frown = ControlFrame.ClampScalar(Frown);
        LipPucker = ControlFrame.ClampScalar(LipPucker);
        LipFunnel = ControlFrame.ClampScalar(LipFunnel);
        LipSuck = ControlFrame.ClampScalar(LipSuck);
        CheekPuffSuck = ControlFrame.ClampSigned(CheekPuffSuck);
        CheekSquint = ControlFrame.ClampScalar(CheekSquint);
        NoseSneer = ControlFrame.ClampScalar(NoseSneer);

        if (LinkEyeYaw)
        {
            RightEyeYaw = LeftEyeYaw;
        }

        if (LinkEyePitch)
        {
            RightEyePitch = LeftEyePitch;
        }

        if (LinkEyeBlink)
        {
            RightEyeBlink = LeftEyeBlink;
        }

        if (LinkBrowRaise)
        {
            RightBrowRaise = LeftBrowRaise;
        }

        if (LinkBrowLower)
        {
            RightBrowLower = LeftBrowLower;
        }
    }

    public ControlFrame ToControlFrame() => new ControlFrame
    {
        LeftEyeYaw = LeftEyeYaw,
        RightEyeYaw = RightEyeYaw,
        LeftEyePitch = LeftEyePitch,
        RightEyePitch = RightEyePitch,
        LeftEyeBlink = LeftEyeBlink,
        RightEyeBlink = RightEyeBlink,
        LeftBrowRaise = LeftBrowRaise,
        RightBrowRaise = RightBrowRaise,
        LeftBrowLower = LeftBrowLower,
        RightBrowLower = RightBrowLower,
        JawOpen = JawOpen,
        JawSideways = JawSideways,
        JawForwardBack = JawForwardBack,
        MouthOpen = MouthOpen,
        Smile = Smile,
        Frown = Frown,
        LipPucker = LipPucker,
        LipFunnel = LipFunnel,
        LipSuck = LipSuck,
        CheekPuffSuck = CheekPuffSuck,
        CheekSquint = CheekSquint,
        NoseSneer = NoseSneer
    }.Clamp();

    public void ResetSection(ResetSection section)
    {
        switch (section)
        {
            case global::VirtualFaceTracking.Shared.ResetSection.Eyes:
                LeftEyeYaw = 0f;
                RightEyeYaw = 0f;
                LeftEyePitch = 0f;
                RightEyePitch = 0f;
                LeftEyeBlink = 0f;
                RightEyeBlink = 0f;
                break;
            case global::VirtualFaceTracking.Shared.ResetSection.Brows:
                LeftBrowRaise = 0f;
                RightBrowRaise = 0f;
                LeftBrowLower = 0f;
                RightBrowLower = 0f;
                break;
            case global::VirtualFaceTracking.Shared.ResetSection.Face:
                JawOpen = 0f;
                JawSideways = 0f;
                JawForwardBack = 0f;
                MouthOpen = 0f;
                Smile = 0f;
                Frown = 0f;
                LipPucker = 0f;
                LipFunnel = 0f;
                LipSuck = 0f;
                CheekPuffSuck = 0f;
                CheekSquint = 0f;
                NoseSneer = 0f;
                break;
        }

        Clamp();
    }
}

public sealed class SimulationState
{
    public bool Enabled { get; set; }
    public float Intensity { get; set; } = 0.35f;
    public float Speed { get; set; } = 0.35f;
    public bool SimulateEyes { get; set; } = true;
    public bool SimulateBlink { get; set; } = true;
    public bool SimulateBrows { get; set; } = true;
    public bool SimulateFace { get; set; } = true;

    public SimulationState DeepClone() => new()
    {
        Enabled = Enabled,
        Intensity = Intensity,
        Speed = Speed,
        SimulateEyes = SimulateEyes,
        SimulateBlink = SimulateBlink,
        SimulateBrows = SimulateBrows,
        SimulateFace = SimulateFace
    };

    public void Clamp()
    {
        Intensity = ControlFrame.ClampScalar(Intensity);
        Speed = ControlFrame.ClampScalar(Speed);
    }

    public void Reset()
    {
        Enabled = false;
        Intensity = 0.35f;
        Speed = 0.35f;
        SimulateEyes = true;
        SimulateBlink = true;
        SimulateBrows = true;
        SimulateFace = true;
    }
}

public sealed class AdvancedOverride
{
    public bool UseOverride { get; set; }
    public float Value { get; set; }

    public AdvancedOverride DeepClone() => new()
    {
        UseOverride = UseOverride,
        Value = Value
    };
}

public sealed class AdvancedOverrideState
{
    public Dictionary<string, AdvancedOverride> Shapes { get; set; } = new(StringComparer.Ordinal);

    public AdvancedOverrideState DeepClone() => new()
    {
        Shapes = Shapes.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.DeepClone(),
            StringComparer.Ordinal)
    };

    public void EnsureCatalog(IEnumerable<string> shapeNames)
    {
        foreach (var shape in shapeNames)
        {
            if (!Shapes.ContainsKey(shape))
            {
                Shapes[shape] = new AdvancedOverride();
            }
        }
    }

    public void Clamp()
    {
        foreach (var entry in Shapes.Values)
        {
            entry.Value = ControlFrame.ClampScalar(entry.Value);
        }
    }

    public void Reset()
    {
        foreach (var entry in Shapes.Values)
        {
            entry.UseOverride = false;
            entry.Value = 0f;
        }
    }

    public static AdvancedOverrideState CreateDefault(IEnumerable<string> shapeNames)
    {
        var state = new AdvancedOverrideState();
        state.EnsureCatalog(shapeNames);
        return state;
    }
}

public sealed class TrackerRuntimeState
{
    public string PipeName { get; set; } = IPC.PipeProtocol.DefaultPipeName;
    public bool OutputEnabled { get; set; }
    public bool EyeOutputAllowed { get; set; }
    public bool ExpressionOutputAllowed { get; set; }
    public bool GuiConnected { get; set; }
    public bool GuiLaunchedByModule { get; set; }
    public DateTimeOffset LastGuiSeenUtc { get; set; } = DateTimeOffset.MinValue;
    public GuiSessionState Gui { get; set; } = new();
    public ManualControlState Manual { get; set; } = new();
    public SimulationState Simulation { get; set; } = new();
    public AdvancedOverrideState AdvancedOverrides { get; set; } = AdvancedOverrideState.CreateDefault(Mapping.VirtualExpressionCatalog.AllShapeNames);

    public TrackerRuntimeState DeepClone() => new()
    {
        PipeName = PipeName,
        OutputEnabled = OutputEnabled,
        EyeOutputAllowed = EyeOutputAllowed,
        ExpressionOutputAllowed = ExpressionOutputAllowed,
        GuiConnected = GuiConnected,
        GuiLaunchedByModule = GuiLaunchedByModule,
        LastGuiSeenUtc = LastGuiSeenUtc,
        Gui = Gui.DeepClone(),
        Manual = Manual.DeepClone(),
        Simulation = Simulation.DeepClone(),
        AdvancedOverrides = AdvancedOverrides.DeepClone()
    };

    public void Clamp()
    {
        Manual.Clamp();
        Simulation.Clamp();
        AdvancedOverrides.EnsureCatalog(Mapping.VirtualExpressionCatalog.AllShapeNames);
        AdvancedOverrides.Clamp();
    }
}
