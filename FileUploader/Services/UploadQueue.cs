using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

public sealed class UploadQueue : IDisposable
{
    private readonly ConcurrentQueue<(string path, Func<CancellationToken, Task> job)> _queue = new();
    private readonly SemaphoreSlim _pumpGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _paused;

    public event Action<string>? OnQueued;
    public event Action<string>? OnStarted;
    public event Action<string>? OnCompleted;
    public event Action<string, Exception>? OnFailed;
    public event Action<string>? OnCanceled;  // NEW

    public Task EnqueueAsync(string filePath, Func<CancellationToken, Task> work)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.Enqueue((filePath, async ct =>
        {
            try
            {
                OnStarted?.Invoke(filePath);
                await work(ct);
                OnCompleted?.Invoke(filePath);
                tcs.TrySetResult(null);
            }
            catch (OperationCanceledException)
            {
                OnCanceled?.Invoke(filePath);
                tcs.TrySetCanceled(ct);
            }
            catch (Exception ex)
            {
                OnFailed?.Invoke(filePath, ex);
                tcs.TrySetException(ex);
            }
        }
        ));

        OnQueued?.Invoke(filePath);
        _ = PumpAsync();
        return tcs.Task;
    }

    private async Task PumpAsync()
    {
        if (!await _pumpGate.WaitAsync(0)) return;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (_paused)
                {
                    await Task.Delay(80, _cts.Token);
                    continue;
                }

                if (_queue.TryDequeue(out var item))
                {
                    await item.job(_cts.Token);
                    continue;
                }

                break;
            }
        }
        finally
        {
            _pumpGate.Release();
        }
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    public void Dispose() => _cts.Cancel();
}
