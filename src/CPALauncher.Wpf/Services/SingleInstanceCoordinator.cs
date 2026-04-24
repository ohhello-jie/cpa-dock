using System.IO.Pipes;
using System.Text.Json;

namespace CPALauncher.Services;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = @"Local\CPADock.CPALauncher.SingleInstance";
    private const string PipeName = "CPADock.CPALauncher.Activate";

    private readonly Mutex? _mutex;
    private CancellationTokenSource? _listenCancellation;
    private Task? _listenTask;
    private Action<string[]>? _onActivationRequested;

    private SingleInstanceCoordinator(bool isPrimary, Mutex? mutex)
    {
        IsPrimary = isPrimary;
        _mutex = mutex;
    }

    public bool IsPrimary { get; }

    public static SingleInstanceCoordinator Create()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstanceCoordinator(isPrimary: true, mutex);
        }

        mutex.Dispose();
        return new SingleInstanceCoordinator(isPrimary: false, mutex: null);
    }

    public void StartListening(Action<string[]> onActivationRequested)
    {
        if (!IsPrimary || _listenTask != null)
        {
            return;
        }

        _onActivationRequested = onActivationRequested;
        _listenCancellation = new CancellationTokenSource();
        _listenTask = Task.Run(() => ListenAsync(_listenCancellation.Token));
    }

    public void RequestActivation(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);

            client.Connect(timeout: 700);
            using var writer = new StreamWriter(client);
            writer.Write(JsonSerializer.Serialize(args));
        }
        catch
        {
            // 已有实例存在时，本进程仍应直接退出；激活失败不应产生第二个启动器。
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server);
                var payload = await reader.ReadToEndAsync();
                var args = DeserializeArgs(payload);
                _onActivationRequested?.Invoke(args);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private static string[] DeserializeArgs(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(payload) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Dispose()
    {
        _listenCancellation?.Cancel();
        _listenCancellation?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
