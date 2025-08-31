using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class StorageUploader : IStorageUploader
{
    private readonly HttpClient _http;

    public StorageUploader()
    {
        _http = new HttpClient();
    }

    public async Task<string> PutChunkAsync(string presignedUrl, Stream content, long contentLength, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, presignedUrl);
        using var sc = new StreamContent(content);
        sc.Headers.ContentLength = contentLength;
        req.Content = sc;

        var r = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        // Surface full S3 error body (was “Failed: response” before)
        if (!r.IsSuccessStatusCode)
        {
            string body = string.Empty;
            try { body = await r.Content.ReadAsStringAsync(ct); } catch { /* ignore */ }
            throw new HttpRequestException(
                $"S3 PUT failed: {(int)r.StatusCode} {r.ReasonPhrase}. Body: {body}");
        }

        string etag = null;

        if (r.Headers.ETag != null)
            etag = r.Headers.ETag.Tag?.Trim('"');
        else if (r.Headers.Contains("ETag"))
            foreach (var v in r.Headers.GetValues("ETag")) { etag = v?.Trim('"'); break; }

        if (string.IsNullOrEmpty(etag))
            throw new InvalidOperationException("Missing ETag from S3 part upload response.");

        return etag;
    }
}
