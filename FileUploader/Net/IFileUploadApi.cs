using FileUploader.Dtos;
using FileUploader.Dtos.Request;
using FileUploader.Dtos.Responses;
using System.Threading;
using System.Threading.Tasks;

public interface IFileUploadApi
{
    Task<StartSessionResponse> StartSessionAsync(StartSessionRequest request, CancellationToken ct);
    Task<RegisterFileResponse> RegisterFileAsync(string sessionId, RegisterFileRequest request, CancellationToken ct);
    Task<PresignPartUrlResponse> PresignPartUrlAsync(string fileId, PresignPartUrlRequest request, CancellationToken ct);
    Task CompleteFileAsync(string fileId, CompleteFileRequest request, CancellationToken ct);
    Task<SessionStatusResponse> GetSessionStatusAsync(string sessionId, CancellationToken ct);
}
