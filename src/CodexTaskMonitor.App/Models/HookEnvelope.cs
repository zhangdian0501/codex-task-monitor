using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexTaskMonitor.App.Models;

public sealed class HookEnvelope
{
    [JsonPropertyName("messageId")]
    public string MessageId { get; init; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("eventName")]
    public string EventName { get; init; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; init; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("permissionMode")]
    public string PermissionMode { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }
}

public sealed class PipeResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
