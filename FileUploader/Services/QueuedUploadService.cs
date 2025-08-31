using System;
using System.Threading;
using System.Threading.Tasks;

public sealed class QueuedUploadService : IDisposable
{
    private readonly IUploadManager _manager;
    private readonly UploadQueue _queue = new();

    public event Action<string> OnQueued;             // filePath
    public event Action<string> OnStarted;            // filePath
    public event Action<string, int> OnProgress;      // filePath, percent
    public event Action<string> OnCompleted;          // filePath
    public event Action<string, Exception> OnFailed;  // filePath, error

    public QueuedUploadService(IUploadManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));

        _queue.OnQueued += fp => OnQueued?.Invoke(fp);
        _queue.OnStarted += fp => OnStarted?.Invoke(fp);
        _queue.OnCompleted += fp => OnCompleted?.Invoke(fp);
        _queue.OnFailed += (fp, ex) => OnFailed?.Invoke(fp, ex);
    }

    public Task EnqueueFileAsync(string userId, string filePath, CancellationToken ct = default)
    {
        return _queue.EnqueueAsync(filePath, async token =>
        {
            await _manager.StartUploadAsync(
                userId,
                new[] { filePath },
                (fp, p) => OnProgress?.Invoke(fp, p),
                token);
        });
    }

    public void PauseAll()
    {
        _queue.Pause();   // freeze future jobs
        _manager.Pause(); // cancel the in-flight call and stop after current chunk
    }

    public void ResumeAll() => _queue.Resume();

    public void Dispose() => _queue.Dispose();
}
