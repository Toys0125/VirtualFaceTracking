using VirtualFaceTracking.Shared.Abstractions;

namespace VirtualFaceTracking.Shared.Mapping;

public sealed class VirtualExpressionMapper : IVirtualExpressionMapper
{
    public MappedTrackingFrame Map(ManualControlState manual, SimulationOffsets offsets, AdvancedOverrideState overrides)
    {
        var composed = Compose(manual, offsets);
        var frame = MappedTrackingFrame.Neutral();

        frame.Eyes.Left.Yaw = composed.LeftEyeYaw;
        frame.Eyes.Right.Yaw = composed.RightEyeYaw;
        frame.Eyes.Left.Pitch = composed.LeftEyePitch;
        frame.Eyes.Right.Pitch = composed.RightEyePitch;
        frame.Eyes.Left.Openness = 1f - composed.LeftEyeBlink;
        frame.Eyes.Right.Openness = 1f - composed.RightEyeBlink;

        ApplyLeftRight(frame.Shapes, composed.LeftBrowRaise, "BrowInnerUpLeft", "BrowOuterUpLeft");
        ApplyLeftRight(frame.Shapes, composed.RightBrowRaise, "BrowInnerUpRight", "BrowOuterUpRight");

        ApplySingle(frame.Shapes, composed.LeftBrowLower, "BrowLowererLeft", "BrowPinchLeft");
        ApplySingle(frame.Shapes, composed.RightBrowLower, "BrowLowererRight", "BrowPinchRight");

        frame.Shapes["JawOpen"] = composed.JawOpen;

        if (composed.JawSideways > 0f)
        {
            frame.Shapes["JawRight"] = composed.JawSideways;
        }
        else if (composed.JawSideways < 0f)
        {
            frame.Shapes["JawLeft"] = Math.Abs(composed.JawSideways);
        }

        if (composed.JawForwardBack > 0f)
        {
            frame.Shapes["JawForward"] = composed.JawForwardBack;
        }
        else if (composed.JawForwardBack < 0f)
        {
            frame.Shapes["JawBackward"] = Math.Abs(composed.JawForwardBack);
        }

        ApplySingle(frame.Shapes, composed.MouthOpen,
            "MouthUpperDeepenLeft",
            "MouthUpperDeepenRight",
            "MouthUpperUpLeft",
            "MouthUpperUpRight",
            "MouthLowerDownLeft",
            "MouthLowerDownRight");

        ApplySingle(frame.Shapes, composed.Smile,
            "MouthCornerPullLeft",
            "MouthCornerPullRight",
            "MouthCornerSlantLeft",
            "MouthCornerSlantRight");

        ApplySingle(frame.Shapes, composed.Frown, "MouthFrownLeft", "MouthFrownRight");
        ApplySingle(frame.Shapes, composed.LipPucker,
            "LipPuckerUpperLeft",
            "LipPuckerUpperRight",
            "LipPuckerLowerLeft",
            "LipPuckerLowerRight");
        ApplySingle(frame.Shapes, composed.LipFunnel,
            "LipFunnelUpperLeft",
            "LipFunnelUpperRight",
            "LipFunnelLowerLeft",
            "LipFunnelLowerRight");
        ApplySingle(frame.Shapes, composed.LipSuck,
            "LipSuckUpperLeft",
            "LipSuckUpperRight",
            "LipSuckLowerLeft",
            "LipSuckLowerRight",
            "LipSuckCornerLeft",
            "LipSuckCornerRight");

        if (composed.CheekPuffSuck > 0f)
        {
            ApplySingle(frame.Shapes, composed.CheekPuffSuck, "CheekPuffLeft", "CheekPuffRight");
        }
        else if (composed.CheekPuffSuck < 0f)
        {
            ApplySingle(frame.Shapes, Math.Abs(composed.CheekPuffSuck), "CheekSuckLeft", "CheekSuckRight");
        }

        ApplySingle(frame.Shapes, composed.CheekSquint, "CheekSquintLeft", "CheekSquintRight");
        ApplySingle(frame.Shapes, composed.NoseSneer, "NoseSneerLeft", "NoseSneerRight");

        overrides.EnsureCatalog(VirtualExpressionCatalog.AllShapeNames);
        overrides.Clamp();

        foreach (var entry in overrides.Shapes)
        {
            if (entry.Value.UseOverride)
            {
                frame.Shapes[entry.Key] = entry.Value.Value;
            }
        }

        return frame;
    }

    public static ControlFrame Compose(ManualControlState manual, SimulationOffsets offsets)
    {
        manual.Clamp();
        offsets.Clamp();
        return new ControlFrame
        {
            LeftEyeYaw = manual.LeftEyeYaw + offsets.LeftEyeYaw,
            RightEyeYaw = manual.RightEyeYaw + offsets.RightEyeYaw,
            LeftEyePitch = manual.LeftEyePitch + offsets.LeftEyePitch,
            RightEyePitch = manual.RightEyePitch + offsets.RightEyePitch,
            LeftEyeBlink = manual.LeftEyeBlink + offsets.LeftEyeBlink,
            RightEyeBlink = manual.RightEyeBlink + offsets.RightEyeBlink,
            LeftBrowRaise = manual.LeftBrowRaise + offsets.LeftBrowRaise,
            RightBrowRaise = manual.RightBrowRaise + offsets.RightBrowRaise,
            LeftBrowLower = manual.LeftBrowLower + offsets.LeftBrowLower,
            RightBrowLower = manual.RightBrowLower + offsets.RightBrowLower,
            JawOpen = manual.JawOpen + offsets.JawOpen,
            JawSideways = manual.JawSideways + offsets.JawSideways,
            JawForwardBack = manual.JawForwardBack + offsets.JawForwardBack,
            MouthOpen = manual.MouthOpen + offsets.MouthOpen,
            Smile = manual.Smile + offsets.Smile,
            Frown = manual.Frown + offsets.Frown,
            LipPucker = manual.LipPucker + offsets.LipPucker,
            LipFunnel = manual.LipFunnel + offsets.LipFunnel,
            LipSuck = manual.LipSuck + offsets.LipSuck,
            CheekPuffSuck = manual.CheekPuffSuck + offsets.CheekPuffSuck,
            CheekSquint = manual.CheekSquint + offsets.CheekSquint,
            NoseSneer = manual.NoseSneer + offsets.NoseSneer
        }.Clamp();
    }

    private static void ApplyLeftRight(IDictionary<string, float> shapes, float value, string leftA, string leftB)
    {
        shapes[leftA] = value;
        shapes[leftB] = value;
    }

    private static void ApplySingle(IDictionary<string, float> shapes, float value, params string[] names)
    {
        foreach (var name in names)
        {
            shapes[name] = value;
        }
    }
}
