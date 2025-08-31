using System.IO;
using System.Threading;
using System.Threading.Tasks;

public interface IStorageUploader
{
    // Returns the ETag string from S3 response
    Task<string> PutChunkAsync(string presignedUrl, Stream content, long contentLength, CancellationToken ct);
}
