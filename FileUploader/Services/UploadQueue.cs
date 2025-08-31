using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

public sealed class UploadQueue : IDisposable
{
    private readonly ConcurrentQueue<Func<CancellationToken, Task>> _queue = new();
    private readonly SemaphoreSlim _pumpGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _paused;

    public event Action<string> OnQueued;             // filePath
    public event Action<string> OnStarted;            // filePath
    public event Action<string> OnCompleted;          // filePath
    public event Action<string, Exception> OnFailed;  // filePath, error

    public Task EnqueueAsync(string filePath, Func<CancellationToken, Task> work)
    {
        if (work is null) throw new ArgumentNullException(nameof(work));
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.Enqueue(async token =>
        {
            try
            {
                OnStarted?.Invoke(filePath);
                await work(token);
                OnCompleted?.Invoke(filePath);
                tcs.TrySetResult(null);
            }
            catch (OperationCanceledException) { tcs.TrySetCanceled(token); }
            catch (Exception ex) { OnFailed?.Invoke(filePath, ex); tcs.TrySetException(ex); }
        });

        OnQueued?.Invoke(filePath);
        _ = PumpAsync();
        return tcs.Task;
    }

    private async Task PumpAsync()
    {
        if (!await _pumpGate.WaitAsync(0)) return; // single active pump
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (_paused) { await Task.Delay(100, _cts.Token); continue; }
                if (!_queue.TryDequeue(out var job)) break;
                await job(_cts.Token);
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
