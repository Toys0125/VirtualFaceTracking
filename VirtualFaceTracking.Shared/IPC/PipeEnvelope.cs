using System.Text.Json;
using System.Text.Json.Serialization;

namespace VirtualFaceTracking.Shared.IPC;

public static class PipeMessageTypes
{
    public const string Hello = "Hello";
    public const string StateSnapshot = "StateSnapshot";
    public const string PatchManualState = "PatchManualState";
    public const string PatchSimulationState = "PatchSimulationState";
    public const string PatchAdvancedOverrides = "PatchAdvancedOverrides";
    public const string SetOutputEnabled = "SetOutputEnabled";
    public const string ResetSection = "ResetSection";
    public const string Shutdown = "Shutdown";
    public const string Ping = "Ping";
}

public sealed class PipeEnvelope
{
    public string MessageType { get; set; } = string.Empty;
    public JsonElement Payload { get; set; }

    public static PipeEnvelope Create<T>(string messageType, T payload) => new()
    {
        MessageType = messageType,
        Payload = JsonSerializer.SerializeToElement(payload, PipeProtocol.JsonOptions)
    };

    public T? GetPayload<T>()
    {
        if (Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return default;
        }

        return Payload.Deserialize<T>(PipeProtocol.JsonOptions);
    }
}

public sealed class HelloMessage
{
    public string ClientName { get; set; } = "VirtualFaceTracking.Gui";
    public int ProcessId { get; set; } = Environment.ProcessId;
}

public sealed class StateSnapshotMessage
{
    public TrackerRuntimeState State { get; set; } = new();
}

public sealed class PatchManualStateMessage
{
    public ManualControlState? Manual { get; set; }
    public GuiSessionState? Gui { get; set; }
}

public sealed class PatchSimulationStateMessage
{
    public SimulationState? Simulation { get; set; }
}

public sealed class PatchAdvancedOverridesMessage
{
    public AdvancedOverrideState? AdvancedOverrides { get; set; }
}

public sealed class SetOutputEnabledMessage
{
    public bool Enabled { get; set; }
}

public sealed class ResetSectionMessage
{
    [JsonConverter(typeof(JsonStringEnumConverter<ResetSection>))]
    public ResetSection Section { get; set; }
}

public sealed class ShutdownMessage
{
    public bool CloseGui { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class PingMessage
{
    public DateTimeOffset SentAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public static class PipeProtocol
{
    public const string DefaultPipeName = "VirtualFaceTracking.Pipe";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    static PipeProtocol()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static async Task WriteAsync(StreamWriter writer, PipeEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    public static async Task<PipeEnvelope?> ReadAsync(StreamReader reader, CancellationToken cancellationToken = default)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(line)
            ? null
            : JsonSerializer.Deserialize<PipeEnvelope>(line, JsonOptions);
    }
}
