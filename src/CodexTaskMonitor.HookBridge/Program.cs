using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const string pipeName = "CodexTaskMonitor.HookBridge.v1";
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var utf8 = new UTF8Encoding(false);

string input;
try
{
    using var inputReader = new StreamReader(
        Console.OpenStandardInput(),
        utf8,
        detectEncodingFromByteOrderMarks: true);
    input = await inputReader.ReadToEndAsync();
}
catch
{
    return 0;
}

JsonDocument document;
try
{
    document = JsonDocument.Parse(input);
}
catch
{
    return 0;
}

using (document)
{
    var root = document.RootElement;
    var envelope = new HookEnvelope
    {
        MessageId = Guid.NewGuid().ToString("N"),
        SessionId = GetString(root, "session_id") ?? "unknown",
        EventName = GetString(root, "hook_event_name") ?? "Unknown",
        Cwd = GetString(root, "cwd") ?? string.Empty,
        Model = GetString(root, "model") ?? string.Empty,
        PermissionMode = GetString(root, "permission_mode") ?? string.Empty,
        Payload = root.Clone()
    };

    try
    {
        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await pipe.ConnectAsync(connectTimeout.Token);

        using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(envelope, jsonOptions));

        var responseWait = envelope.EventName is "PermissionRequest" or "DialogRequest" or "Stop"
            ? TimeSpan.FromMinutes(9)
            : TimeSpan.FromSeconds(2);
        using var responseTimeout = new CancellationTokenSource(responseWait);
        var responseLine = await reader.ReadLineAsync(responseTimeout.Token);
        var response = responseLine is null
            ? null
            : JsonSerializer.Deserialize<PipeResponse>(responseLine, jsonOptions);

        if (string.Equals(envelope.EventName, "DialogRequest", StringComparison.Ordinal))
        {
            if (response is not null)
            {
                await WriteJsonAsync(response, jsonOptions, utf8);
            }

            return 0;
        }

        if (string.Equals(envelope.EventName, "Stop", StringComparison.Ordinal))
        {
            var reason = response?.Message;
            if (string.Equals(response?.Decision, "block", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(reason))
            {
                await WriteJsonAsync(
                    new { decision = "block", reason },
                    jsonOptions,
                    utf8);
            }

            return 0;
        }

        if (!string.Equals(envelope.EventName, "PermissionRequest", StringComparison.Ordinal) ||
            response?.Decision is null)
        {
            return 0;
        }

        object? output = response.Decision switch
        {
            "allow" => new
            {
                hookSpecificOutput = new
                {
                    hookEventName = "PermissionRequest",
                    decision = new { behavior = "allow" }
                }
            },
            "deny" => new
            {
                hookSpecificOutput = new
                {
                    hookEventName = "PermissionRequest",
                    decision = new
                    {
                        behavior = "deny",
                        message = response.Message ?? "用户拒绝了该请求。"
                    }
                }
            },
            _ => null
        };

        if (output is not null)
        {
            await WriteJsonAsync(output, jsonOptions, utf8);
        }

        return 0;
    }
    catch
    {
        return 0;
    }
}

static async Task WriteJsonAsync(object value, JsonSerializerOptions options, Encoding encoding)
{
    await using var outputWriter = new StreamWriter(Console.OpenStandardOutput(), encoding)
    {
        AutoFlush = true
    };
    await outputWriter.WriteLineAsync(JsonSerializer.Serialize(value, options));
}

static string? GetString(JsonElement element, string name)
{
    return element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;
}

internal sealed class HookEnvelope
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

internal sealed class PipeResponse
{
    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
