using FileUploadClient.Wpf.Util;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class StorageUploader : IStorageUploader, IDisposable
{
    private readonly HttpClient _http;

    public StorageUploader()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.ExpectContinue = false;
    }

    public async Task<string> UploadPartAsync(string presignedUrl,
                                              Stream partStream,
                                              long contentLength,
                                              CancellationToken ct)
    {
        NetLog.Line($"REQUEST  PUT {NetLog.Trunc(presignedUrl, 200)}");
        NetLog.Line($"Request Headers: Content-Length={contentLength}");

        using var req = new HttpRequestMessage(HttpMethod.Put, presignedUrl);
        req.Content = new StreamContent(partStream);
        req.Content.Headers.ContentLength = contentLength;

        using HttpResponseMessage resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        string? etag = null;
        if (resp.Headers.TryGetValues("ETag", out var values))
            etag = values.FirstOrDefault();
        else if (resp.Headers.ETag != null)
            etag = resp.Headers.ETag.Tag;

        NetLog.Line($"RESPONSE ({(int)resp.StatusCode}) {resp.StatusCode}");
        NetLog.Line($"ETag Header: {etag ?? "(none)"}");

        resp.EnsureSuccessStatusCode();

        if (string.IsNullOrWhiteSpace(etag))
            throw new InvalidOperationException("Upload succeeded but no ETag was returned by storage.");

        return etag.Trim().Trim('"'); // normalize
    }

    public void Dispose() => _http.Dispose();
}
