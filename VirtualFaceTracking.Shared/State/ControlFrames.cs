using VirtualFaceTracking.Shared.Mapping;

namespace VirtualFaceTracking.Shared;

public class ControlFrame
{
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

    public ControlFrame DeepClone() => new()
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
    };

    public ControlFrame Clamp()
    {
        LeftEyeYaw = ClampSigned(LeftEyeYaw);
        RightEyeYaw = ClampSigned(RightEyeYaw);
        LeftEyePitch = ClampSigned(LeftEyePitch);
        RightEyePitch = ClampSigned(RightEyePitch);
        LeftEyeBlink = ClampScalar(LeftEyeBlink);
        RightEyeBlink = ClampScalar(RightEyeBlink);

        LeftBrowRaise = ClampScalar(LeftBrowRaise);
        RightBrowRaise = ClampScalar(RightBrowRaise);
        LeftBrowLower = ClampScalar(LeftBrowLower);
        RightBrowLower = ClampScalar(RightBrowLower);

        JawOpen = ClampScalar(JawOpen);
        JawSideways = ClampSigned(JawSideways);
        JawForwardBack = ClampSigned(JawForwardBack);
        MouthOpen = ClampScalar(MouthOpen);
        Smile = ClampScalar(Smile);
        Frown = ClampScalar(Frown);
        LipPucker = ClampScalar(LipPucker);
        LipFunnel = ClampScalar(LipFunnel);
        LipSuck = ClampScalar(LipSuck);
        CheekPuffSuck = ClampSigned(CheekPuffSuck);
        CheekSquint = ClampScalar(CheekSquint);
        NoseSneer = ClampScalar(NoseSneer);
        return this;
    }

    public static float ClampSigned(float value) => Math.Clamp(value, -1f, 1f);

    public static float ClampScalar(float value) => Math.Clamp(value, 0f, 1f);
}

public sealed class SimulationOffsets : ControlFrame
{
}

public sealed class EyeOutput
{
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Openness { get; set; }
}

public sealed class EyeOutputFrame
{
    public EyeOutput Left { get; set; } = new();
    public EyeOutput Right { get; set; } = new();
}

public sealed class MappedTrackingFrame
{
    public EyeOutputFrame Eyes { get; set; } = new();
    public Dictionary<string, float> Shapes { get; set; } = VirtualExpressionCatalog
        .AllShapeNames
        .ToDictionary(shape => shape, _ => 0f, StringComparer.Ordinal);

    public static MappedTrackingFrame Neutral() => new()
    {
        Eyes = new EyeOutputFrame
        {
            Left = new EyeOutput { Openness = 1f },
            Right = new EyeOutput { Openness = 1f }
        }
    };
}
