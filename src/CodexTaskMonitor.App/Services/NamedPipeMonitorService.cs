using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CodexTaskMonitor.App.Models;

namespace CodexTaskMonitor.App.Services;

public sealed class NamedPipeMonitorService : IAsyncDisposable
{
    public const string PipeName = "CodexTaskMonitor.HookBridge.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, Task> _clients = new();
    private Task? _acceptLoop;
    private int _clientId;

    public Func<HookEnvelope, Task<PipeResponse>>? MessageReceived { get; set; }

    public event Action<string>? StatusChanged;

    public void Start()
    {
        if (_acceptLoop is not null)
        {
            return;
        }

        _acceptLoop = AcceptLoopAsync(_stop.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        StatusChanged?.Invoke("正在监听");

        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellationToken);
                var id = Interlocked.Increment(ref _clientId);
                var clientTask = HandleClientAsync(pipe, cancellationToken);
                pipe = null;
                _clients[id] = clientTask;
                _ = clientTask.ContinueWith(
                    _ => _clients.TryRemove(id, out Task? _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"监听异常：{ex.Message}");
                try
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                pipe?.Dispose();
            }
        }

        StatusChanged?.Invoke("已停止");
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe)
        using (var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true))
        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
        {
            PipeResponse response;
            try
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                var message = line is null
                    ? null
                    : JsonSerializer.Deserialize<HookEnvelope?>(line, JsonOptions);

                if (message is null || string.IsNullOrWhiteSpace(message.SessionId))
                {
                    response = new PipeResponse { Success = false, Message = "消息格式无效" };
                }
                else
                {
                    response = MessageReceived is null
                        ? new PipeResponse()
                        : await MessageReceived(message);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                response = new PipeResponse { Success = false, Message = ex.Message };
            }

            await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.WaitAsync(StopTimeout);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
            }
        }

        var clients = _clients.Values.ToArray();
        if (clients.Length > 0)
        {
            try
            {
                await Task.WhenAll(clients).WaitAsync(StopTimeout);
            }
            catch
            {
            }
        }

        _stop.Dispose();
    }
}
