using FileUploadClient.Wpf.Config;
using FileUploader.Dtos;
using FileUploader.Dtos.Request;
using FileUploader.Dtos.Responses;
using System;
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
        ChunkBytes = AppConfig.ChunkBytes;
        StopRequested = false;
    }

    public async Task<string> StartUploadAsync(string userId, string[] filePaths, Action<string, int> reportProgress, CancellationToken ct)
    {
        // 1) Start session
        StartSessionRequest sreq = new StartSessionRequest();
        sreq.UserId = userId;
        StartSessionResponse sresp = await _api.StartSessionAsync(sreq, ct);
        string sessionId = sresp.SessionId;

        int i = 0;
        while (i < filePaths.Length)
        {
            if (StopRequested) break;

            string path = filePaths[i];
            FileInfo fi = new FileInfo(path);
            long size = fi.Length;

            // 2) compute chunkCount = ceil(size / ChunkBytes)
            int chunkCount = (int)(size / (long)ChunkBytes);
            if ((size % (long)ChunkBytes) != 0) chunkCount = chunkCount + 1;
            if (chunkCount < 1) chunkCount = 1;

            // 3) register file
            RegisterFileRequest rreq = new RegisterFileRequest();
            rreq.FileName = fi.Name;
            rreq.FileSize = size;
            rreq.ChunkCount = chunkCount;

            RegisterFileResponse rresp = await _api.RegisterFileAsync(sessionId, rreq, ct);
            string fileId = rresp.FileId;
            string uploadId = rresp.UploadId;

            // keep parts + etags
            System.Collections.Generic.List<CompleteFileRequest.PartETag> parts =
                new System.Collections.Generic.List<CompleteFileRequest.PartETag>();

            // 4) loop chunks
            FileStream fs = File.OpenRead(path);
            long sent = 0;
            int partNumber = 1;

            try
            {
                while (sent < size)
                {
                    if (StopRequested) break;

                    long remaining = size - sent;
                    int len = (int)Math.Min((long)ChunkBytes, remaining);

                    byte[] buffer = new byte[len];
                    int read = await fs.ReadAsync(buffer, 0, len, ct);
                    if (read == 0) break;

                    // 5) presign this part
                    PresignPartUrlRequest preq = new PresignPartUrlRequest();
                    preq.PartNumber = partNumber;

                    PresignPartUrlResponse pres = await _api.PresignPartUrlAsync(fileId, preq, ct);

                    // 6) PUT the part to S3 and capture ETag
                    MemoryStream ms = new MemoryStream(buffer, 0, read, false, true);
                    string etag = await _uploader.PutChunkAsync(pres.Url, ms, read, ct);
                    ms.Dispose();

                    // 7) add to parts
                    CompleteFileRequest.PartETag pe = new CompleteFileRequest.PartETag();
                    pe.PartNumber = partNumber;
                    pe.ETag = etag;
                    parts.Add(pe);

                    sent = sent + read;
                    partNumber = partNumber + 1;

                    // 8) update progress 0..100
                    int percent = (int)Math.Round(100.0 * (double)sent / (double)size);
                    if (reportProgress != null) reportProgress(path, percent);
                }
            }
            finally
            {
                fs.Dispose();
            }

            // 9) complete file (only if not paused)
            if (!StopRequested)
            {
                CompleteFileRequest creq = new CompleteFileRequest();
                creq.UploadId = uploadId;
                creq.Parts = parts;

                await _api.CompleteFileAsync(fileId, creq, ct);

                if (reportProgress != null) reportProgress(path, 100);
            }

            i = i + 1;
        }

        return sessionId;
    }

    public void Pause()
    {
        StopRequested = true;
    }
}
