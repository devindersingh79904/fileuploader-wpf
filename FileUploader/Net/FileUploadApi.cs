using FileUploadClient.Wpf.Config;
using FileUploadClient.Wpf.Util;
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
        // Every request will be logged by LoggingHandler
        var handler = new LoggingHandler(new HttpClientHandler());
        _http = new HttpClient(handler) { BaseAddress = new Uri(AppConfig.BaseUrl) };
    }

    public async Task<StartSessionResponse> StartSessionAsync(StartSessionRequest request, CancellationToken ct)
    {
        string body = JsonConvert.SerializeObject(request);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, "start") { Content = content };

        using HttpResponseMessage r = await _http.SendAsync(req, ct);
        r.EnsureSuccessStatusCode();
        string s = await r.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<StartSessionResponse>(s);
    }

    public async Task<RegisterFileResponse> RegisterFileAsync(string sessionId, RegisterFileRequest request, CancellationToken ct)
    {
        string body = JsonConvert.SerializeObject(request);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{sessionId}/files") { Content = content };

        using HttpResponseMessage r = await _http.SendAsync(req, ct);
        r.EnsureSuccessStatusCode();
        string s = await r.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<RegisterFileResponse>(s);
    }

    public async Task<PresignPartUrlResponse> PresignPartUrlAsync(string fileId, PresignPartUrlRequest request, CancellationToken ct)
    {
        string body = JsonConvert.SerializeObject(request);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"files/{fileId}/parts/url") { Content = content };

        using HttpResponseMessage r = await _http.SendAsync(req, ct);
        r.EnsureSuccessStatusCode();
        string s = await r.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<PresignPartUrlResponse>(s);
    }

    public async Task CompleteFileAsync(string fileId, CompleteFileRequest request, CancellationToken ct)
    {
        string body = JsonConvert.SerializeObject(request);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"files/{fileId}/complete") { Content = content };

        using HttpResponseMessage r = await _http.SendAsync(req, ct);
        r.EnsureSuccessStatusCode();
    }

    public async Task<SessionStatusResponse> GetSessionStatusAsync(string sessionId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{sessionId}/status");
        using HttpResponseMessage r = await _http.SendAsync(req, ct);
        r.EnsureSuccessStatusCode();
        string s = await r.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<SessionStatusResponse>(s);
    }

    public async Task<FilePartsResponse> GetFilePartsAsync(string fileId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"files/{fileId}/parts");
        using HttpResponseMessage r = await _http.SendAsync(req, ct);
        r.EnsureSuccessStatusCode();
        string s = await r.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<FilePartsResponse>(s);
    }

    // ---- Session-level controls ----
    public async Task PauseSessionAsync(string sessionId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"{sessionId}/pause");
        using HttpResponseMessage r = await _http.SendAsync(req, ct);
        r.EnsureSuccessStatusCode();
    }

    public async Task ResumeSessionAsync(string sessionId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"{sessionId}/resume");
        using HttpResponseMessage r = await _http.SendAsync(req, ct);
        r.EnsureSuccessStatusCode();
    }

    public async Task CompleteSessionAsync(string sessionId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"{sessionId}/complete");
        using HttpResponseMessage r = await _http.SendAsync(req, ct);
        r.EnsureSuccessStatusCode();
    }
}
