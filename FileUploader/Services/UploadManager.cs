using FileUploadClient.Wpf.Config;
using FileUploader.Dtos;
using FileUploader.Dtos.Request;
using FileUploader.Dtos.Responses;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class UploadManager : IUploadManager
{
    private readonly IFileUploadApi _api;
    private readonly IStorageUploader _uploader;

    public bool StopRequested;
    public int ChunkBytes;

    public UploadManager(IFileUploadApi api, IStorageUploader uploader)
    {
        _api = api;
        _uploader = uploader;
        ChunkBytes = AppConfig.ChunkBytes; // e.g., 5 * 1024 * 1024
        StopRequested = false;
    }

    public async Task<string> StartUploadAsync(
        string userId,
        string[] filePaths,
        Action<string, int> reportProgress,
        CancellationToken ct)
    {
        if (filePaths == null || filePaths.Length == 0)
            throw new ArgumentException("No files to upload.", nameof(filePaths));

        // Start or reuse session server-side
        var sresp = await _api.StartSessionAsync(new StartSessionRequest { UserId = userId }, ct);
        string sessionId = sresp.SessionId;

        for (int i = 0; i < filePaths.Length; i++)
        {
            if (StopRequested) break;
            var path = filePaths[i];

            try
            {
                ct.ThrowIfCancellationRequested();

                if (!File.Exists(path))
                    throw new FileNotFoundException("File not found", path);

                var fi = new FileInfo(path);
                long size = fi.Length;

                // compute part count
                int chunkCount = (int)(size / (long)ChunkBytes);
                if ((size % (long)ChunkBytes) != 0) chunkCount++;
                if (chunkCount < 1) chunkCount = 1;

                var rresp = await _api.RegisterFileAsync(sessionId, new RegisterFileRequest
                {
                    FileName = fi.Name,
                    FileSize = size,
                    ChunkCount = chunkCount
                }, ct);

                string fileId = rresp.FileId;
                string uploadId = rresp.UploadId;

                var parts = new List<CompleteFileRequest.PartETag>(chunkCount);

                using var fs = File.OpenRead(path);
                long sent = 0;
                int partNumber = 1;

                while (sent < size)
                {
                    if (StopRequested) break;
                    ct.ThrowIfCancellationRequested();

                    long remaining = size - sent;
                    int len = (int)Math.Min((long)ChunkBytes, remaining);

                    byte[] buffer = new byte[len];
                    int read = await fs.ReadAsync(buffer, 0, len, ct);
                    if (read == 0) break;

                    // IMPORTANT: Many backends require the exact part size for signing
                    var pres = await _api.PresignPartUrlAsync(fileId, new PresignPartUrlRequest
                    {
                        PartNumber = partNumber,
                        //PartSizeBytes = read           // <--- key fix
                    }, ct);

                    using var ms = new MemoryStream(buffer, 0, read, writable: false, publiclyVisible: true);
                    string etag = await _uploader.PutChunkAsync(pres.Url, ms, read, ct);

                    parts.Add(new CompleteFileRequest.PartETag
                    {
                        PartNumber = partNumber,
                        ETag = etag
                    });

                    sent += read;
                    partNumber++;

                    int percent = (int)Math.Round(100.0 * sent / Math.Max(1, size));
                    reportProgress?.Invoke(path, Math.Clamp(percent, 0, 100));
                }

                if (!StopRequested)
                {
                    await _api.CompleteFileAsync(fileId, new CompleteFileRequest
                    {
                        UploadId = uploadId,
                        Parts = parts
                    }, ct);

                    reportProgress?.Invoke(path, 100);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // propagate cancel
            }
            catch (Exception)
            {
                // mark failed but continue with next file
                reportProgress?.Invoke(path, 0);
            }
        }

        return sessionId;
    }

    public void Pause() => StopRequested = true;
}
