using System.Collections.Generic;
using Newtonsoft.Json;

namespace FileUploader.Dtos.Responses
{
    public class FilePartsResponse
    {
        [JsonProperty("fileId")]
        public string FileId { get; set; } = string.Empty;

        [JsonProperty("s3Key")]
        public string S3Key { get; set; } = string.Empty;

        // Needed for CompleteFile
        [JsonProperty("uploadId")]
        public string UploadId { get; set; } = string.Empty;

        [JsonProperty("totalChunks")]
        public int TotalChunks { get; set; }

        // ← EXACT name from backend: uploadedPartNumbers
        [JsonProperty("uploadedPartNumbers")]
        public List<int> UploadedPartNumbers { get; set; } = new();

        [JsonProperty("pendingPartNumbers")]
        public List<int> PendingPartNumbers { get; set; } = new();

        // ← EXACT name from backend: uploadedParts (each has partNumber + eTag)
        [JsonProperty("uploadedParts")]
        public List<UploadedPart> UploadedParts { get; set; } = new();

        public class UploadedPart
        {
            [JsonProperty("partNumber")]
            public int PartNumber { get; set; }

            [JsonProperty("eTag")]
            public string ETag { get; set; } = string.Empty;
        }
    }
}
