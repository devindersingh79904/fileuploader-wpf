using FileUploadClient.Wpf.Config;
using FileUploader.Dtos;
using FileUploader.Dtos.Request;
using FileUploader.Dtos.Responses;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class FileUploadApi : IFileUploadApi
{
    private readonly HttpClient _http;

    public FileUploadApi()
    {
        _http = new HttpClient();
        // IMPORTANT: AppConfig.BaseUrl must end with "/api/v1/upload/"
        _http.BaseAddress = new Uri(AppConfig.BaseUrl);
        // _http.Timeout = TimeSpan.FromSeconds(100);
    }

    public async Task<StartSessionResponse> StartSessionAsync(StartSessionRequest request, CancellationToken ct)
    {
        var r = await _http.PostAsync("start", AsJson(request), ct);
        if (!r.IsSuccessStatusCode) await ThrowWithBody(r, ct);
        return JsonConvert.DeserializeObject<StartSessionResponse>(await r.Content.ReadAsStringAsync());
    }

    public async Task<RegisterFileResponse> RegisterFileAsync(string sessionId, RegisterFileRequest request, CancellationToken ct)
    {
        var r = await _http.PostAsync($"{sessionId}/files", AsJson(request), ct);
        if (!r.IsSuccessStatusCode) await ThrowWithBody(r, ct);
        return JsonConvert.DeserializeObject<RegisterFileResponse>(await r.Content.ReadAsStringAsync());
    }

    public async Task<PresignPartUrlResponse> PresignPartUrlAsync(string fileId, PresignPartUrlRequest request, CancellationToken ct)
    {
        // NOTE: correct route is under files/{fileId}/parts/url
        var r = await _http.PostAsync($"files/{fileId}/parts/url", AsJson(request), ct);
        if (!r.IsSuccessStatusCode) await ThrowWithBody(r, ct);
        return JsonConvert.DeserializeObject<PresignPartUrlResponse>(await r.Content.ReadAsStringAsync());
    }

    public async Task CompleteFileAsync(string fileId, CompleteFileRequest request, CancellationToken ct)
    {
        // NOTE: correct route is files/{fileId}/complete (PATCH)
        var req = new HttpRequestMessage(HttpMethod.Patch, $"files/{fileId}/complete")
        {
            Content = AsJson(request)
        };
        var r = await _http.SendAsync(req, ct);
        if (!r.IsSuccessStatusCode) await ThrowWithBody(r, ct);
    }

    public async Task<SessionStatusResponse> GetSessionStatusAsync(string sessionId, CancellationToken ct)
    {
        var r = await _http.GetAsync($"{sessionId}/status", ct);
        if (!r.IsSuccessStatusCode) await ThrowWithBody(r, ct);
        return JsonConvert.DeserializeObject<SessionStatusResponse>(await r.Content.ReadAsStringAsync());
    }

    // ---- helpers ----
    private static StringContent AsJson(object o)
        => new StringContent(JsonConvert.SerializeObject(o), Encoding.UTF8, "application/json");

    private static async Task ThrowWithBody(HttpResponseMessage r, CancellationToken ct)
    {
        string body = string.Empty;
        try { body = await r.Content.ReadAsStringAsync(ct); } catch { /* ignore */ }
        throw new HttpRequestException(
            $"API {r.RequestMessage?.Method} {r.RequestMessage?.RequestUri} failed: {(int)r.StatusCode} {r.ReasonPhrase}. Body: {body}");
    }
}
