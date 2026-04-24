namespace VirtualFaceTracking.Shared.Abstractions;

public interface IVirtualTrackerStateStore
{
    TrackerRuntimeState Load();
    void Save(TrackerRuntimeState state);
}

public interface IVirtualSimulationEngine
{
    SimulationOffsets Compute(TrackerRuntimeState state, TimeSpan elapsed);
}

public interface IVirtualExpressionMapper
{
    MappedTrackingFrame Map(ManualControlState manual, SimulationOffsets offsets, AdvancedOverrideState overrides);
}
