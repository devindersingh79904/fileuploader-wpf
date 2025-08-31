using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

public class StorageUploader : IStorageUploader
{
    private readonly HttpClient _http;

    public StorageUploader()
    {
        _http = new HttpClient();
    }

    public async Task<string> PutChunkAsync(string presignedUrl, Stream content, long contentLength, CancellationToken ct)
    {
        HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Put, presignedUrl);
        StreamContent sc = new StreamContent(content);
        sc.Headers.ContentLength = contentLength;
        req.Content = sc;

        HttpResponseMessage r = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        r.EnsureSuccessStatusCode();

        string etag = null;
        if (r.Headers.ETag != null)
        {
            etag = r.Headers.ETag.Tag; // often quoted
        }
        else
        {
            if (r.Headers.Contains("ETag"))
            {
                foreach (var v in r.Headers.GetValues("ETag"))
                {
                    etag = v;
                    break;
                }
            }
        }
        return etag;
    }
}
