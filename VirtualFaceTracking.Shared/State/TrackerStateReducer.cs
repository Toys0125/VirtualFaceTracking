using VirtualFaceTracking.Shared.IPC;
using VirtualFaceTracking.Shared.Mapping;

namespace VirtualFaceTracking.Shared;

public static class TrackerStateReducer
{
    public static bool Apply(TrackerRuntimeState state, PipeEnvelope envelope)
    {
        switch (envelope.MessageType)
        {
            case PipeMessageTypes.Hello:
                state.GuiConnected = true;
                state.LastGuiSeenUtc = DateTimeOffset.UtcNow;
                return false;

            case PipeMessageTypes.Ping:
                state.GuiConnected = true;
                state.LastGuiSeenUtc = DateTimeOffset.UtcNow;
                return false;

            case PipeMessageTypes.PatchManualState:
                var manualPatch = envelope.GetPayload<PatchManualStateMessage>() ?? new PatchManualStateMessage();
                if (manualPatch.Manual is not null)
                {
                    state.Manual = manualPatch.Manual.DeepClone();
                }

                if (manualPatch.Gui is not null)
                {
                    state.Gui = manualPatch.Gui.DeepClone();
                }

                state.GuiConnected = true;
                state.LastGuiSeenUtc = DateTimeOffset.UtcNow;
                state.Clamp();
                return false;

            case PipeMessageTypes.PatchSimulationState:
                var simulationPatch = envelope.GetPayload<PatchSimulationStateMessage>() ?? new PatchSimulationStateMessage();
                if (simulationPatch.Simulation is not null)
                {
                    state.Simulation = simulationPatch.Simulation.DeepClone();
                }

                state.GuiConnected = true;
                state.LastGuiSeenUtc = DateTimeOffset.UtcNow;
                state.Clamp();
                return false;

            case PipeMessageTypes.PatchAdvancedOverrides:
                var advancedPatch = envelope.GetPayload<PatchAdvancedOverridesMessage>() ?? new PatchAdvancedOverridesMessage();
                if (advancedPatch.AdvancedOverrides is not null)
                {
                    state.AdvancedOverrides = advancedPatch.AdvancedOverrides.DeepClone();
                    state.AdvancedOverrides.EnsureCatalog(VirtualExpressionCatalog.AllShapeNames);
                }

                state.GuiConnected = true;
                state.LastGuiSeenUtc = DateTimeOffset.UtcNow;
                state.Clamp();
                return false;

            case PipeMessageTypes.SetOutputEnabled:
                var enabledPatch = envelope.GetPayload<SetOutputEnabledMessage>() ?? new SetOutputEnabledMessage();
                state.OutputEnabled = enabledPatch.Enabled;
                state.GuiConnected = true;
                state.LastGuiSeenUtc = DateTimeOffset.UtcNow;
                return false;

            case PipeMessageTypes.ResetSection:
                var reset = envelope.GetPayload<ResetSectionMessage>() ?? new ResetSectionMessage();
                ApplyReset(state, reset.Section);
                state.GuiConnected = true;
                state.LastGuiSeenUtc = DateTimeOffset.UtcNow;
                return false;

            case PipeMessageTypes.Shutdown:
                return true;

            default:
                return false;
        }
    }

    private static void ApplyReset(TrackerRuntimeState state, ResetSection section)
    {
        switch (section)
        {
            case ResetSection.Eyes:
            case ResetSection.Brows:
            case ResetSection.Face:
                state.Manual.ResetSection(section);
                break;
            case ResetSection.Simulation:
                state.Simulation.Reset();
                break;
            case ResetSection.All:
                state.Manual.ResetSection(ResetSection.Eyes);
                state.Manual.ResetSection(ResetSection.Brows);
                state.Manual.ResetSection(ResetSection.Face);
                state.Simulation.Reset();
                state.AdvancedOverrides.Reset();
                state.OutputEnabled = false;
                break;
        }

        state.Clamp();
    }
}
