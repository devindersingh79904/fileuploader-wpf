using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 1-at-a-time FIFO queue with Pause/Resume.
/// If a job is paused mid-file, it’s requeued to the FRONT so Resume() continues that file first.
/// </summary>
public sealed class UploadQueue : IDisposable
{
    private readonly ConcurrentQueue<(string path, Func<CancellationToken, Task> job)> _queue = new();
    private readonly ConcurrentQueue<(string path, Func<CancellationToken, Task> job)> _front = new(); // priority requeue
    private readonly SemaphoreSlim _pumpGate = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _paused;

    public event Action<string>? OnQueued;
    public event Action<string>? OnStarted;
    public event Action<string>? OnCompleted;
    public event Action<string>? OnCanceled;                   // <-- added
    public event Action<string, Exception>? OnFailed;

    public Task EnqueueAsync(string filePath, Func<CancellationToken, Task> work)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
        if (work is null) throw new ArgumentNullException(nameof(work));

        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task Wrapped(CancellationToken ct)
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
                // When paused, put this job back at the FRONT so it resumes first.
                if (_paused && !_cts.IsCancellationRequested)
                {
                    EnqueueFront(filePath, Wrapped);
                    OnCanceled?.Invoke(filePath);             // <-- added
                }
                tcs.TrySetCanceled(ct);
            }
            catch (Exception ex)
            {
                OnFailed?.Invoke(filePath, ex);
                tcs.TrySetException(ex);
            }
        }

        _queue.Enqueue((filePath, Wrapped));
        OnQueued?.Invoke(filePath);
        _ = PumpAsync();
        return tcs.Task;
    }

    private void EnqueueFront(string filePath, Func<CancellationToken, Task> wrapped)
    {
        _front.Enqueue((filePath, wrapped));
        OnQueued?.Invoke(filePath);
    }

    private async Task PumpAsync()
    {
        if (!await _pumpGate.WaitAsync(0)) return; // single pump
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (_paused)
                {
                    await Task.Delay(80, _cts.Token);
                    continue;
                }

                if (_front.TryDequeue(out var pri))
                {
                    await pri.job(_cts.Token);
                    continue;
                }

                if (_queue.TryDequeue(out var item))
                {
                    await item.job(_cts.Token);
                    continue;
                }

                break; // nothing left
            }
        }
        finally
        {
            _pumpGate.Release();
        }
    }

    public void Pause() => _paused = true;

    public void Resume()
    {
        _paused = false;
        _ = PumpAsync(); // kick the pump if idle
    }

    public void Dispose() => _cts.Cancel();
}
