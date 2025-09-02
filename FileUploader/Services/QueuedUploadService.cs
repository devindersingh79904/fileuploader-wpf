using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

public sealed class QueuedUploadService : IDisposable
{
    private readonly IUploadManager _manager;
    private readonly UploadQueue _queue = new();

    public event Action<string>? OnQueued;
    public event Action<string>? OnStarted;
    public event Action<string, int>? OnProgress;
    public event Action<string>? OnCompleted;
    public event Action<string, Exception>? OnFailed;

    public event Action<string>? OnCanceled; // optional for UI

    private readonly ConcurrentDictionary<string, string> _userByFile = new();
    private readonly ConcurrentDictionary<string, byte> _pausedFiles = new();

    public QueuedUploadService(IUploadManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));

        _queue.OnQueued += fp => OnQueued?.Invoke(fp);
        _queue.OnStarted += fp => OnStarted?.Invoke(fp);
        _queue.OnCompleted += fp => OnCompleted?.Invoke(fp);
        _queue.OnFailed += (fp, ex) => OnFailed?.Invoke(fp, ex);
        _queue.OnCanceled += fp =>
        {
            _pausedFiles[fp] = 1;
            OnCanceled?.Invoke(fp);
        };
    }

    public Task EnqueueFileAsync(string userId, string filePath, CancellationToken ct = default)
    {
        _userByFile[filePath] = userId;

        return _queue.EnqueueAsync(filePath, async token =>
        {
            await _manager.StartUploadAsync(
                userId,
                new[] { filePath },
                (fp, p) => OnProgress?.Invoke(fp, p),
                token
            );
        });
    }

    public void PauseAll()
    {
        _queue.Pause();   // stop starting new jobs
        _manager.Pause(); // cancel current file between parts
    }

    public void ResumeAll()
    {
        _queue.Resume();

        // re-enqueue any files canceled mid-flight
        foreach (var kv in _pausedFiles)
        {
            var fp = kv.Key;
            if (_pausedFiles.TryRemove(fp, out _))
            {
                if (_userByFile.TryGetValue(fp, out var user))
                {
                    _ = EnqueueFileAsync(user, fp, CancellationToken.None);
                }
            }
        }
    }

    public void Dispose() => _queue.Dispose();
}
