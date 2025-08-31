using FileUploadClient.Wpf.Config;
using FileUploader.Dtos;
using FileUploader.Dtos.Request;
using FileUploader.Dtos.Responses;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Sequential uploader with Pause that:
/// - Cancels the in-flight network call immediately
/// - Saves resume state (fileId, uploadId, parts, nextPartNumber, sentBytes)
/// - Resumes from the next chunk when StartUploadAsync is invoked again
/// </summary>
public class UploadManager : IUploadManager
{
    private readonly IFileUploadApi _api;
    private readonly IStorageUploader _uploader;

    public int ChunkBytes { get; set; }

    // Pausing:
    private CancellationTokenSource _currentOpCts;  // cancels the presign/PUT/complete immediately
    private volatile bool _pauseRequested;          // checked between chunks & before/after calls

    // Per-file resume state
    private readonly Dictionary<string, UploadState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed class UploadState
    {
        public string FilePath { get; init; }
        public string FileId { get; set; }
        public string UploadId { get; set; }
        public long FileSize { get; init; }
        public long SentBytes { get; set; }            // successful uploaded bytes
        public int NextPartNumber { get; set; }       // 1-based
        public List<CompleteFileRequest.PartETag> Parts { get; } = new();
        public bool IsComplete => SentBytes >= FileSize;
    }

    public UploadManager(IFileUploadApi api, IStorageUploader uploader)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
        ChunkBytes = AppConfig.ChunkBytes; // e.g., 5 * 1024 * 1024
    }

    public async Task<string> StartUploadAsync(
        string userId,
        string[] filePaths,
        Action<string, int> reportProgress,
        CancellationToken outerCt)
    {
        if (filePaths == null || filePaths.Length == 0)
            throw new ArgumentException("No files to upload.", nameof(filePaths));

        // Clear pause if we are (re)starting
        _pauseRequested = false;

        // Get/reuse session
        var sresp = await _api.StartSessionAsync(new StartSessionRequest { UserId = userId }, outerCt);
        string sessionId = sresp.SessionId;

        foreach (var path in filePaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (!File.Exists(path))
            {
                reportProgress?.Invoke(path, 0);
                continue;
            }

            try
            {
                await UploadSingleFileAsync(sessionId, path, reportProgress, outerCt);
            }
            catch (OperationCanceledException)
            {
                // Pause/Cancel — bubble up so the queue/VM can stop dispatching further items for now.
                throw;
            }
            catch
            {
                // Do not abort the whole batch for one file failure.
                reportProgress?.Invoke(path, 0);
            }
        }

        return sessionId;
    }

    public void Pause()
    {
        // Tell the loop to stop after the current chunk, and cancel the in-flight request now.
        _pauseRequested = true;
        try { _currentOpCts?.Cancel(); } catch { /* ignore */ }
    }

    // ---------------------------------- internals ----------------------------------

    private async Task UploadSingleFileAsync(
        string sessionId,
        string filePath,
        Action<string, int> reportProgress,
        CancellationToken outerCt)
    {
        var fi = new FileInfo(filePath);
        var state = await GetOrCreateStateAsync(sessionId, fi, outerCt).ConfigureAwait(false);

        // progress on entry (resume case)
        ReportProgress(reportProgress, filePath, state.SentBytes, state.FileSize);

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (state.SentBytes > 0) fs.Seek(state.SentBytes, SeekOrigin.Begin);

        while (!state.IsComplete)
        {
            outerCt.ThrowIfCancellationRequested();
            if (_pauseRequested) throw new OperationCanceledException("Paused before presign.");

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            _currentOpCts = linked;

            long remaining = state.FileSize - state.SentBytes;
            int len = (int)Math.Min((long)ChunkBytes, remaining);
            if (len <= 0) break;

            var buffer = new byte[len];
            int read = await fs.ReadAsync(buffer, 0, len, outerCt).ConfigureAwait(false);
            if (read <= 0) break;

            try
            {
                // presign
                var pres = await _api.PresignPartUrlAsync(state.FileId, new PresignPartUrlRequest
                {
                    PartNumber = state.NextPartNumber
                    // If your DTO later includes PartSizeBytes, set it here to 'read'
                }, linked.Token).ConfigureAwait(false);

                if (_pauseRequested) throw new OperationCanceledException("Paused before PUT.");

                // PUT part
                using var ms = new MemoryStream(buffer, 0, read, writable: false, publiclyVisible: true);
                string etag = await _uploader.PutChunkAsync(pres.Url, ms, read, linked.Token).ConfigureAwait(false);

                // record successful part
                state.Parts.Add(new CompleteFileRequest.PartETag
                {
                    PartNumber = state.NextPartNumber,
                    ETag = etag
                });

                state.SentBytes += read;
                state.NextPartNumber++;

                ReportProgress(reportProgress, filePath, state.SentBytes, state.FileSize);

                if (_pauseRequested) throw new OperationCanceledException("Paused after chunk saved.");
            }
            catch (OperationCanceledException)
            {
                // We advanced only on success; state is consistent for resume.
                throw;
            }
            finally
            {
                _currentOpCts = null;
            }
        }

        // finalize if fully sent
        if (state.IsComplete)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            _currentOpCts = linked;

            try
            {
                await _api.CompleteFileAsync(state.FileId, new CompleteFileRequest
                {
                    UploadId = state.UploadId,
                    Parts = state.Parts
                }, linked.Token).ConfigureAwait(false);

                ReportProgress(reportProgress, filePath, state.FileSize, state.FileSize);
                _states.Remove(state.FilePath);
            }
            finally
            {
                _currentOpCts = null;
            }
        }
    }

    private async Task<UploadState> GetOrCreateStateAsync(string sessionId, FileInfo fi, CancellationToken ct)
    {
        if (_states.TryGetValue(fi.FullName, out var existing))
            return existing;

        // compute backend's expected part count
        int chunkCount = (int)(fi.Length / (long)ChunkBytes);
        if ((fi.Length % (long)ChunkBytes) != 0) chunkCount++;
        if (chunkCount < 1) chunkCount = 1;

        var reg = await _api.RegisterFileAsync(sessionId, new RegisterFileRequest
        {
            FileName = fi.Name,
            FileSize = fi.Length,
            ChunkCount = chunkCount
        }, ct).ConfigureAwait(false);

        var state = new UploadState
        {
            FilePath = fi.FullName,
            FileId = reg.FileId,
            UploadId = reg.UploadId,
            FileSize = fi.Length,
            SentBytes = 0,
            NextPartNumber = 1
        };

        _states[fi.FullName] = state;
        return state;
    }

    private static void ReportProgress(Action<string, int> cb, string path, long sent, long total)
    {
        int percent = total <= 0 ? 0 : (int)Math.Round(sent * 100.0 / total);
        if (percent < 0) percent = 0;
        if (percent > 100) percent = 100;
        cb?.Invoke(path, percent);
    }
}
