using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

public sealed class QueuedUploadService
{
    private readonly IUploadManager _uploadManager;

    private readonly ConcurrentQueue<string> _queue = new();

    private Task? _pumpTask;
    private volatile bool _paused;
    private volatile bool _stopping;
    private CancellationTokenSource? _inflightCts;
    private string _lastUserId = "c001";

    // resume the file that was interrupted by Pause first
    private string? _resumeFirst;

    // UI hooks
    public event Action<string>? OnQueued;
    public event Action<string>? OnStarted;
    public event Action<string, int>? OnProgress;
    public event Action<string>? OnCompleted;
    public event Action<string, Exception>? OnFailed;
    public event Action<string>? OnCanceled;

    public QueuedUploadService(IUploadManager uploadManager)
    {
        _uploadManager = uploadManager ?? throw new ArgumentNullException(nameof(uploadManager));
    }

    public Task EnqueueFileAsync(string userId, string filePath, CancellationToken ct = default)
    {
        _lastUserId = string.IsNullOrWhiteSpace(userId) ? _lastUserId : userId;

        // Fire Queued BEFORE enqueue to avoid a race that could overwrite "Uploading".
        OnQueued?.Invoke(filePath);

        _queue.Enqueue(filePath);

        EnsurePump();  // make sure worker is running
        return Task.CompletedTask;
    }

    public void PauseAll()
    {
        _paused = true;

        try { _uploadManager.Pause(); } catch { /* ignore */ }

        try { _inflightCts?.Cancel(); } catch { /* ignore */ }
    }

    public void ResumeAll()
    {
        _paused = false;
        _stopping = false;
        EnsurePump();  // kick worker again
    }

    public void CancelAll()
    {
        _stopping = true;
        _paused = false;

        while (_queue.TryDequeue(out _)) { /* drop everything */ }

        try { _inflightCts?.Cancel(); } catch { /* ignore */ }
    }

    // ---------------- worker ----------------

    private void EnsurePump()
    {
        if (_pumpTask == null || _pumpTask.IsCompleted)
            _pumpTask = Task.Run(ProcessQueueAsync);
    }

    private async Task ProcessQueueAsync()
    {
        while (!_stopping)
        {
            if (_paused)
            {
                await Task.Delay(120);
                continue;
            }

            string? filePath = null;

            if (_resumeFirst != null)
            {
                filePath = _resumeFirst;
                _resumeFirst = null;
            }
            else
            {
                if (!_queue.TryDequeue(out filePath))
                {
                    // nothing left; exit until next Enqueue/Resume
                    return;
                }
            }

            OnStarted?.Invoke(filePath);

            using var cts = new CancellationTokenSource();
            _inflightCts = cts;

            try
            {
                await _uploadManager.StartUploadAsync(
                    _lastUserId,
                    new[] { filePath },
                    (fp, p) => OnProgress?.Invoke(fp, p),
                    cts.Token);

                OnCompleted?.Invoke(filePath);
            }
            catch (OperationCanceledException)
            {
                _resumeFirst = filePath;   // resume this file first
                OnCanceled?.Invoke(filePath);
            }
            catch (Exception ex)
            {
                OnFailed?.Invoke(filePath, ex);
            }
            finally
            {
                _inflightCts = null;
            }
        }
    }
}
