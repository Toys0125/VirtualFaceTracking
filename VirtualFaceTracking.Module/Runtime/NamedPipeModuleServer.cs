using System.IO.Pipes;
using VirtualFaceTracking.Shared;
using VirtualFaceTracking.Shared.Diagnostics;
using VirtualFaceTracking.Shared.IPC;

namespace VirtualFaceTracking.Module.Runtime;

public sealed class NamedPipeModuleServer(
    string pipeName,
    Func<TrackerRuntimeState> snapshotProvider,
    Action<PipeEnvelope> messageHandler,
    Action<bool> connectionChanged) : IDisposable
{
    private readonly string _pipeName = pipeName;
    private readonly Func<TrackerRuntimeState> _snapshotProvider = snapshotProvider;
    private readonly Action<PipeEnvelope> _messageHandler = messageHandler;
    private readonly Action<bool> _connectionChanged = connectionChanged;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private Task? _listenTask;
    private NamedPipeServerStream? _stream;
    private StreamWriter? _writer;

    public bool Start()
    {
        try
        {
            _listenTask = Task.Run(ListenLoopAsync, _cts.Token);
            VirtualTrackerDiagnostics.Write("ModulePipe", $"Listening on pipe '{_pipeName}'");
            return true;
        }
        catch
        {
            VirtualTrackerDiagnostics.Write("ModulePipe", $"Failed to start listener on pipe '{_pipeName}'");
            return false;
        }
    }

    public async Task SendAsync(PipeEnvelope envelope)
    {
        if (_writer is null)
        {
            return;
        }

        await _writeLock.WaitAsync(_cts.Token);
        try
        {
            if (_writer is not null)
            {
                await PipeProtocol.WriteAsync(_writer, envelope, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task SendSnapshotAsync() => SendAsync(PipeEnvelope.Create(
        PipeMessageTypes.StateSnapshot,
        new StateSnapshotMessage { State = _snapshotProvider() }));

    public void Stop()
    {
        _cts.Cancel();

        try
        {
            _stream?.Dispose();
        }
        catch
        {
        }

        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            NamedPipeServerStream? currentStream = null;

            try
            {
                currentStream = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await currentStream.WaitForConnectionAsync(_cts.Token);

                _stream = currentStream;
                _writer = new StreamWriter(currentStream) { AutoFlush = true };
                using var reader = new StreamReader(currentStream);

                _connectionChanged(true);
                VirtualTrackerDiagnostics.Write("ModulePipe", "GUI connected to pipe");
                await SendSnapshotAsync();

                var readTask = ReadLoopAsync(reader, connectionCts.Token);
                var broadcastTask = BroadcastLoopAsync(connectionCts.Token);
                await readTask;
                connectionCts.Cancel();
                await broadcastTask;
            }
            catch (OperationCanceledException)
            {
                connectionCts.Cancel();
            }
            catch (IOException)
            {
                connectionCts.Cancel();
                VirtualTrackerDiagnostics.Write("ModulePipe", "Pipe I/O fault; reconnecting listener");
            }
            catch
            {
                connectionCts.Cancel();
                VirtualTrackerDiagnostics.Write("ModulePipe", "Unexpected pipe fault; reconnecting listener");
            }
            finally
            {
                _writer = null;
                _stream = null;
                _connectionChanged(false);
                VirtualTrackerDiagnostics.Write("ModulePipe", "GUI disconnected from pipe");
                currentStream?.Dispose();
            }
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var envelope = await PipeProtocol.ReadAsync(reader, cancellationToken);
            if (envelope is null)
            {
                break;
            }

            if (!string.Equals(envelope.MessageType, PipeMessageTypes.Ping, StringComparison.Ordinal))
            {
                VirtualTrackerDiagnostics.Write("ModulePipe", $"Received {envelope.MessageType}");
            }
            _messageHandler(envelope);
            await SendSnapshotAsync();
        }
    }

    private async Task BroadcastLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    await SendSnapshotAsync();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
        _writeLock.Dispose();
    }
}
