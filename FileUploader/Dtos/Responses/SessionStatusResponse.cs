using System.Collections.Generic;
using Newtonsoft.Json;

namespace FileUploader.Dtos.Responses
{
    public class SessionStatusResponse
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        // Session status (e.g., IN_PROGRESS, PAUSED, COMPLETED)
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("files")]
        public List<FileStatusItem> Files { get; set; } = new();

        public class FileStatusItem
        {
            [JsonProperty("fileId")]
            public string FileId { get; set; }

            [JsonProperty("fileName")]
            public string FileName { get; set; }

            [JsonProperty("totalChunks")]
            public int TotalChunks { get; set; }

            [JsonProperty("uploadedChunks")]
            public int UploadedChunks { get; set; }

            // IMPORTANT: this is called "status" in the server JSON
            [JsonProperty("status")]
            public string Status { get; set; }

            [JsonProperty("pending")]
            public List<int> Pending { get; set; } = new();
        }
    }
}
