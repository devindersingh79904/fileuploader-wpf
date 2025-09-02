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

    private const int DefaultPartSizeBytes = 5 * 1024 * 1024; // 5 MB

    // store state for resume
    private class FileState
    {
        public string SessionId = "";
        public string FileId = "";
        public string UploadId = "";
        public long FileSize;
        public int PartSize;
        public int ChunkCount;
    }

    private readonly ConcurrentDictionary<string, FileState> _state = new();
    private CancellationTokenSource? _pauseCts;

    public UploadManager(IFileUploadApi api, IStorageUploader uploader)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
    }

    public async Task<string> StartUploadAsync(
        string userId,
        string[] filePaths,
        Action<string, int> onProgress,
        CancellationToken ct)
    {
        // always reuse session if possible
        var session = await _api.StartSessionAsync(new StartSessionRequest { UserId = userId }, ct);
        string sessionId = session.SessionId;

        foreach (var path in filePaths)
        {
            ct.ThrowIfCancellationRequested();

            var fi = new FileInfo(path);
            if (!fi.Exists) throw new FileNotFoundException("File not found", path);

            int partSize = DefaultPartSizeBytes;
            int chunkCount = (int)Math.Ceiling(fi.Length / (double)partSize);

            // retrieve or create state
            var st = _state.GetOrAdd(path, _ => new FileState
            {
                SessionId = sessionId,
                PartSize = partSize,
                FileSize = fi.Length,
                ChunkCount = chunkCount
            });

            if (string.IsNullOrEmpty(st.FileId))
            {
                // register only once
                var reg = await _api.RegisterFileAsync(sessionId, new RegisterFileRequest
                {
                    FileName = fi.Name,
                    FileSize = fi.Length,
                    ChunkCount = chunkCount
                }, ct);

                st.FileId = reg.FileId;
                st.UploadId = reg.UploadId;
            }

            // ask server which parts already uploaded
            var existing = await _api.GetFilePartsAsync(st.FileId, ct);
            var alreadyUploaded = new HashSet<int>(existing?.UploadedPartNumbers ?? Enumerable.Empty<int>());

            var partsForComplete = new List<CompleteFileRequest.PartETag>();
            if (existing?.UploadedParts != null)
            {
                foreach (var p in existing.UploadedParts)
                {
                    partsForComplete.Add(new CompleteFileRequest.PartETag
                    {
                        PartNumber = p.PartNumber,
                        ETag = p.ETag
                    });
                }
            }

            long sent = 0;
            for (int pn = 1; pn <= st.ChunkCount; pn++)
            {
                if (alreadyUploaded.Contains(pn))
                {
                    long sizeThisPart = pn < st.ChunkCount
                        ? st.PartSize
                        : st.FileSize - (long)(st.PartSize) * (st.ChunkCount - 1);

                    sent += sizeThisPart;
                }
            }

            using var fs = fi.OpenRead();
            fs.Position = sent;

            using (_pauseCts = new CancellationTokenSource())
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _pauseCts.Token))
            {
                var wct = linkedCts.Token;

                for (int partNumber = 1; partNumber <= st.ChunkCount; partNumber++)
                {
                    wct.ThrowIfCancellationRequested();

                    if (alreadyUploaded.Contains(partNumber))
                        continue; // skip uploaded parts

                    int toRead = (int)Math.Min(st.PartSize, st.FileSize - sent);
                    var buffer = new byte[toRead];
                    int read = await fs.ReadAsync(buffer, 0, toRead, wct);
                    if (read <= 0) break;

                    var presign = await _api.PresignPartUrlAsync(st.FileId, new PresignPartUrlRequest
                    {
                        PartNumber = partNumber
                    }, wct);

                    using var ms = new MemoryStream(buffer, 0, read, writable: false, publiclyVisible: true);
                    string etag = await _uploader.UploadPartAsync(presign.Url, ms, read, wct);

                    partsForComplete.Add(new CompleteFileRequest.PartETag
                    {
                        PartNumber = partNumber,
                        ETag = etag
                    });

                    sent += read;
                    int percent = (int)Math.Round((sent / (double)st.FileSize) * 100.0);
                    onProgress?.Invoke(path, percent);
                }
            }

            // complete only when all chunks uploaded
            if (partsForComplete.Count == st.ChunkCount)
            {
                await _api.CompleteFileAsync(st.FileId, new CompleteFileRequest
                {
                    UploadId = st.UploadId,
                    Parts = partsForComplete.OrderBy(p => p.PartNumber).ToList()
                }, ct);

                onProgress?.Invoke(path, 100);
            }
        }

        return sessionId;
    }

    public void Pause()
    {
        try { _pauseCts?.Cancel(); } catch { }
    }
}
