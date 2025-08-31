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
        _http.BaseAddress = new Uri(AppConfig.BaseUrl);
        // optional: _http.Timeout = TimeSpan.FromSeconds(100);
    }

    public async Task<StartSessionResponse> StartSessionAsync(StartSessionRequest request, CancellationToken ct)
    {
        string body = JsonConvert.SerializeObject(request);
        StringContent content = new StringContent(body, Encoding.UTF8, "application/json");
        HttpResponseMessage r = await _http.PostAsync("start", content, ct);
        r.EnsureSuccessStatusCode();
        string s = await r.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<StartSessionResponse>(s);
    }

    public async Task<RegisterFileResponse> RegisterFileAsync(string sessionId, RegisterFileRequest request, CancellationToken ct)
    {
        string body = JsonConvert.SerializeObject(request);
        StringContent content = new StringContent(body, Encoding.UTF8, "application/json");
        HttpResponseMessage r = await _http.PostAsync(sessionId + "/files", content, ct);
        r.EnsureSuccessStatusCode();
        string s = await r.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<RegisterFileResponse>(s);
    }

    public async Task<PresignPartUrlResponse> PresignPartUrlAsync(string fileId, PresignPartUrlRequest request, CancellationToken ct)
    {
        string body = JsonConvert.SerializeObject(request);
        StringContent content = new StringContent(body, Encoding.UTF8, "application/json");
        HttpResponseMessage r = await _http.PostAsync(fileId + "/parts/url", content, ct);
        r.EnsureSuccessStatusCode();
        string s = await r.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<PresignPartUrlResponse>(s);
    }

    public async Task CompleteFileAsync(string fileId, CompleteFileRequest request, CancellationToken ct)
    {
        string body = JsonConvert.SerializeObject(request);
        StringContent content = new StringContent(body, Encoding.UTF8, "application/json");
        HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Patch, fileId + "/complete");
        req.Content = content;
        HttpResponseMessage r = await _http.SendAsync(req, ct);
        r.EnsureSuccessStatusCode();
    }

    public async Task<SessionStatusResponse> GetSessionStatusAsync(string sessionId, CancellationToken ct)
    {
        HttpResponseMessage r = await _http.GetAsync(sessionId + "/status", ct);
        r.EnsureSuccessStatusCode();
        string s = await r.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<SessionStatusResponse>(s);
    }
}
