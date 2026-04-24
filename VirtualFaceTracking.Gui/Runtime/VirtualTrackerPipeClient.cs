using System.IO.Pipes;
using VirtualFaceTracking.Shared;
using VirtualFaceTracking.Shared.Diagnostics;
using VirtualFaceTracking.Shared.IPC;

namespace VirtualFaceTracking.Gui.Runtime;

public sealed class VirtualTrackerPipeClient : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private NamedPipeClientStream? _stream;
    private StreamWriter? _writer;
    private Task? _runTask;
    private string _pipeName = PipeProtocol.DefaultPipeName;

    public bool VerboseLoggingEnabled { get; set; }

    public event Action<StateSnapshotMessage>? SnapshotReceived;
    public event Action<ShutdownMessage>? ShutdownRequested;
    public event Action<bool>? ConnectionChanged;

    public void Start(string pipeName)
    {
        _pipeName = pipeName;
        _runTask = Task.Run(RunAsync, _cts.Token);
    }

    public async Task<bool> SendAsync(PipeEnvelope envelope)
    {
        if (_writer is null)
        {
            if (VerboseLoggingEnabled && !string.Equals(envelope.MessageType, PipeMessageTypes.Ping, StringComparison.Ordinal))
            {
                VirtualTrackerDiagnostics.Write("GuiPipe", $"Skipped send for {envelope.MessageType}; writer not ready");
            }
            return false;
        }

        await _writeLock.WaitAsync(_cts.Token);
        try
        {
            if (_writer is not null)
            {
                await PipeProtocol.WriteAsync(_writer, envelope, _cts.Token);
                if (VerboseLoggingEnabled && !string.Equals(envelope.MessageType, PipeMessageTypes.Ping, StringComparison.Ordinal))
                {
                    VirtualTrackerDiagnostics.Write("GuiPipe", $"Sent {envelope.MessageType}");
                }
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            if (!string.Equals(envelope.MessageType, PipeMessageTypes.Ping, StringComparison.Ordinal))
            {
                VirtualTrackerDiagnostics.Write("GuiPipe", $"Send canceled for {envelope.MessageType}");
            }
        }
        catch (IOException)
        {
            if (!string.Equals(envelope.MessageType, PipeMessageTypes.Ping, StringComparison.Ordinal))
            {
                VirtualTrackerDiagnostics.Write("GuiPipe", $"Pipe write failed for {envelope.MessageType}");
            }
        }
        finally
        {
            _writeLock.Release();
        }

        return false;
    }

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(1000, _cts.Token);

                _stream = client;
                _writer = new StreamWriter(client) { AutoFlush = true };
                using var reader = new StreamReader(client);

                VirtualTrackerDiagnostics.Write("GuiPipe", $"Connected to pipe '{_pipeName}'");
                ConnectionChanged?.Invoke(true);

                while (!_cts.IsCancellationRequested && client.IsConnected)
                {
                    var envelope = await PipeProtocol.ReadAsync(reader, _cts.Token);
                    if (envelope is null)
                    {
                        break;
                    }

                    switch (envelope.MessageType)
                    {
                        case PipeMessageTypes.StateSnapshot:
                            var snapshot = envelope.GetPayload<StateSnapshotMessage>();
                            if (snapshot is not null)
                            {
                                if (VerboseLoggingEnabled)
                                {
                                    VirtualTrackerDiagnostics.Write(
                                        "GuiPipe",
                                        $"Received StateSnapshot. OutputEnabled={snapshot.State.OutputEnabled} SimulationEnabled={snapshot.State.Simulation.Enabled}");
                                }
                                SnapshotReceived?.Invoke(snapshot);
                            }

                            break;
                        case PipeMessageTypes.Shutdown:
                            var shutdown = envelope.GetPayload<ShutdownMessage>();
                            if (shutdown is not null)
                            {
                                ShutdownRequested?.Invoke(shutdown);
                            }

                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
                VirtualTrackerDiagnostics.Write("GuiPipe", "Pipe read loop faulted; reconnecting");
            }
            finally
            {
                _writer = null;
                _stream = null;
                VirtualTrackerDiagnostics.Write("GuiPipe", "Disconnected from pipe");
                ConnectionChanged?.Invoke(false);
            }

            try
            {
                await Task.Delay(500, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();

        try
        {
            _stream?.Dispose();
            _runTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _writeLock.Dispose();
        _cts.Dispose();
    }
}
