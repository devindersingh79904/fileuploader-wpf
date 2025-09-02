using FileUploader.Dtos.Request;
using FileUploader.Dtos.Responses;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


public class UploadManager : IUploadManager
{
    private readonly IFileUploadApi _api;
    private readonly IStorageUploader _uploader;
    private readonly FileUploader.LocalState.UploadStateStore _store;

    private const int DefaultPartSizeBytes = 5 * 1024 * 1024; // 5 MB

    private class FileState
    {
        public string SessionId = "";
        public string FileId = "";
        public string UploadId = "";
        public long FileSize;
        public int PartSize;
        public int ChunkCount;
    }

    // filePath -> state
    private readonly ConcurrentDictionary<string, FileState> _state = new();
    private CancellationTokenSource? _pauseCts;

    public UploadManager(IFileUploadApi api, IStorageUploader uploader, FileUploader.LocalState.UploadStateStore store)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<string> StartUploadAsync(
        string userId,
        string[] filePaths,
        Action<string, int> onProgress,
        CancellationToken ct)
    {
        // Ensure session exists (backend returns same active one if already created for this user).
        var session = await _api.StartSessionAsync(new StartSessionRequest { UserId = userId }, ct);
        string sessionId = session.SessionId;

        foreach (var path in filePaths)
        {
            ct.ThrowIfCancellationRequested();

            var fi = new FileInfo(path);
            if (!fi.Exists) throw new FileNotFoundException("File not found", path);

            // Part math
            var ps = DefaultPartSizeBytes;
            var chunks = (int)Math.Ceiling(fi.Length / (double)ps);

            var st = _state.GetOrAdd(path, _ => new FileState
            {
                SessionId = sessionId,
                PartSize = ps,
                FileSize = fi.Length,
                ChunkCount = chunks
            });

            // Load any persisted state (resume)
            var persisted = _store.Load();
            if (persisted.TryGetValue(path, out var saved))
            {
                if (!string.IsNullOrWhiteSpace(saved.FileId)) st.FileId = saved.FileId!;
                if (!string.IsNullOrWhiteSpace(saved.UploadId)) st.UploadId = saved.UploadId!;
                if (saved.TotalParts > 0) st.ChunkCount = saved.TotalParts;
            }

            // Register file if first time
            if (string.IsNullOrEmpty(st.FileId))
            {
                var reg = await _api.RegisterFileAsync(sessionId, new RegisterFileRequest
                {
                    FileName = fi.Name,
                    FileSize = fi.Length,
                    ChunkCount = st.ChunkCount
                }, ct);

                st.FileId = reg.FileId;
                st.UploadId = reg.UploadId;

                Persist(path, st, uploadedParts: 0, progressPercent: 0);
            }

            // Get server-known parts (for resume)
            var existing = await _api.GetFilePartsAsync(st.FileId, ct);

            var alreadyNumbers = new HashSet<int>(
                existing?.UploadedPartNumbers ?? Enumerable.Empty<int>());

            // Normalize existing parts' ETags (quotes removed)
            var existingParts = new List<CompleteFileRequest.PartETag>();
            if (existing?.UploadedParts != null)
            {
                foreach (var p in existing.UploadedParts)
                {
                    existingParts.Add(new CompleteFileRequest.PartETag
                    {
                        PartNumber = p.PartNumber,
                        ETag = CleanETag(p.ETag)
                    });
                }
            }

            // Compute bytes already uploaded (to set stream position/progress)
            long sent = 0;
            for (int pn = 1; pn <= st.ChunkCount; pn++)
            {
                if (alreadyNumbers.Contains(pn))
                {
                    long sizeThisPart = pn < st.ChunkCount
                        ? st.PartSize
                        : st.FileSize - (long)st.PartSize * (st.ChunkCount - 1);
                    sent += sizeThisPart;
                }
            }

            int initialPct = (int)Math.Round((sent / (double)st.FileSize) * 100.0);
            Persist(path, st, alreadyNumbers.Count, initialPct);
            onProgress?.Invoke(path, initialPct);

            var partsForComplete = new List<CompleteFileRequest.PartETag>();

            using var fs = fi.OpenRead();
            fs.Position = sent;

            using (_pauseCts = new CancellationTokenSource())
            using (var workCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _pauseCts.Token))
            {
                var wct = workCts.Token;

                for (int partNumber = 1; partNumber <= st.ChunkCount; partNumber++)
                {
                    wct.ThrowIfCancellationRequested();

                    // Skip parts that the server already has
                    if (alreadyNumbers.Contains(partNumber))
                    {
                        long sizeThisPart = partNumber < st.ChunkCount
                            ? st.PartSize
                            : st.FileSize - (long)st.PartSize * (st.ChunkCount - 1);

                        if (fs.Position + sizeThisPart <= fs.Length)
                            fs.Position += sizeThisPart;

                        sent += sizeThisPart;
                        int pctDone = (int)Math.Round((sent / (double)st.FileSize) * 100.0);
                        onProgress?.Invoke(path, pctDone);
                        Persist(path, st, alreadyNumbers.Count, pctDone);
                        continue;
                    }

                    int toRead = (int)Math.Min(st.PartSize, st.FileSize - sent);
                    var buffer = new byte[toRead];
                    int read = await fs.ReadAsync(buffer, 0, toRead, wct);
                    if (read <= 0) break;

                    var presign = await _api.PresignPartUrlAsync(st.FileId, new PresignPartUrlRequest
                    {
                        PartNumber = partNumber
                    }, wct);

                    using var ms = new MemoryStream(buffer, 0, read, writable: false, publiclyVisible: true);
                    string etagRaw = await _uploader.UploadPartAsync(presign.Url, ms, read, wct);
                    string etag = CleanETag(etagRaw); // <-- remove quotes

                    partsForComplete.Add(new CompleteFileRequest.PartETag
                    {
                        PartNumber = partNumber,
                        ETag = etag
                    });

                    sent += read;
                    int pct = (int)Math.Round((sent / (double)st.FileSize) * 100.0);
                    onProgress?.Invoke(path, pct);

                    int uploadedPartsNow = alreadyNumbers.Count + partsForComplete.Count;
                    Persist(path, st, uploadedPartsNow, pct);

                    wct.ThrowIfCancellationRequested();
                }
            }

            // If all parts known (existing + new), complete the file
            if (partsForComplete.Count + existingParts.Count == st.ChunkCount)
            {
                // Merge, distinct by part number, pick a non-empty ETag, and order ascending
                var finalParts = existingParts
                    .Concat(partsForComplete)
                    .GroupBy(p => p.PartNumber)
                    .Select(g => g.First(x => !string.IsNullOrWhiteSpace(x.ETag)))
                    .OrderBy(p => p.PartNumber)
                    .ToList();

                // Defensive clamp (ensure 1..N with no duplicates)
                finalParts = finalParts
                    .Where(p => p.PartNumber >= 1 && p.PartNumber <= st.ChunkCount)
                    .GroupBy(p => p.PartNumber)
                    .Select(g => g.First())
                    .OrderBy(p => p.PartNumber)
                    .ToList();

                // Try complete with 1 retry after re-fetching server parts (handles races)
                async Task CompleteWithRetryAsync(CancellationToken token)
                {
                    try
                    {
                        await _api.CompleteFileAsync(st.FileId, new CompleteFileRequest
                        {
                            UploadId = st.UploadId,
                            Parts = finalParts
                        }, token);
                    }
                    catch
                    {
                        // Re-fetch and merge again; backend may not have persisted last part yet
                        var fresh = await _api.GetFilePartsAsync(st.FileId, token);
                        // Replace this line:
                        // var merged = (fresh?.UploadedParts ?? Array.Empty<GetFilePartsResponse.UploadedPart>())

                        // With this corrected line:
                        var merged = (fresh?.UploadedParts ?? new List<FilePartsResponse.UploadedPart>())
                            .Select(p => new CompleteFileRequest.PartETag
                            {
                                PartNumber = p.PartNumber,
                                ETag = CleanETag(p.ETag)
                            })
                            .Concat(partsForComplete)
                            .GroupBy(p => p.PartNumber)
                            .Select(g => g.First(x => !string.IsNullOrWhiteSpace(x.ETag)))
                            .OrderBy(p => p.PartNumber)
                            .ToList();

                        await _api.CompleteFileAsync(st.FileId, new CompleteFileRequest
                        {
                            UploadId = st.UploadId,
                            Parts = merged
                        }, token);
                    }
                }

                await CompleteWithRetryAsync(ct);

                // Clean persisted state and surface 100%
                RemoveFromStore(path);
                onProgress?.Invoke(path, 100);
            }
            // else paused or cancelled — persisted state remains for resume
        }

        return sessionId;
    }

    public void Pause()
    {
        try { _pauseCts?.Cancel(); } catch { /* ignore */ }
    }

    // ---------- Helpers ----------

    private static string CleanETag(string etag)
        => string.IsNullOrWhiteSpace(etag) ? etag : etag.Trim().Trim('"');

    private void Persist(string path, FileState st, int uploadedParts, int progressPercent)
    {
        var dict = _store.Load();
        dict[path] = new FileUploader.LocalState.UploadStateDto
        {
            FilePath = path,
            UploadedParts = uploadedParts,
            TotalParts = st.ChunkCount,
            UploadId = st.UploadId,
            FileId = st.FileId,
            ProgressPercent = progressPercent
        };
        _store.Save(dict);
    }

    private void RemoveFromStore(string path)
    {
        var dict = _store.Load();
        if (dict.Remove(path))
            _store.Save(dict);
    }
}
