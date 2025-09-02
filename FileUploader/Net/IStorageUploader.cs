using System.IO;
using System.Threading;
using System.Threading.Tasks;

public interface IStorageUploader
{
    /// <summary>
    /// Upload a single part to the pre-signed URL and return the ETag
    /// returned by the storage provider (e.g., S3).
    /// </summary>
    Task<string> UploadPartAsync(string presignedUrl,
                                 Stream partStream,
                                 long contentLength,
                                 CancellationToken ct);
}
