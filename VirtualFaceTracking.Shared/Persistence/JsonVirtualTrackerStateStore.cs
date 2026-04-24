using System.Text.Json;
using VirtualFaceTracking.Shared.Abstractions;
using VirtualFaceTracking.Shared.IPC;
using VirtualFaceTracking.Shared.Mapping;

namespace VirtualFaceTracking.Shared.Persistence;

public sealed class JsonVirtualTrackerStateStore(string statePath, string? defaultsPath = null) : IVirtualTrackerStateStore
{
    private readonly string _statePath = statePath;
    private readonly string? _defaultsPath = defaultsPath;

    public TrackerRuntimeState Load()
    {
        var state = ReadState(_statePath)
                    ?? ReadState(_defaultsPath)
                    ?? new TrackerRuntimeState();

        state.AdvancedOverrides.EnsureCatalog(VirtualExpressionCatalog.AllShapeNames);
        state.OutputEnabled = false;
        state.GuiConnected = false;
        state.GuiLaunchedByModule = false;
        state.LastGuiSeenUtc = DateTimeOffset.MinValue;
        state.Clamp();
        return state;
    }

    public void Save(TrackerRuntimeState state)
    {
        state.Clamp();
        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_statePath, JsonSerializer.Serialize(state, PipeProtocol.JsonOptions));
    }

    private static TrackerRuntimeState? ReadState(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<TrackerRuntimeState>(File.ReadAllText(path), IPC.PipeProtocol.JsonOptions);
    }
}
