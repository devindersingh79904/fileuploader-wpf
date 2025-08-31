using System;
using System.Threading;
using System.Threading.Tasks;

public interface IUploadManager
{
    // reportProgress(path, percent)
    Task<string> StartUploadAsync(string userId, string[] filePaths, Action<string, int> reportProgress, CancellationToken ct);
    void Pause();
}
