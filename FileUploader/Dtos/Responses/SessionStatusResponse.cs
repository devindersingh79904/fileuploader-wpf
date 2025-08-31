using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileUploader.Dtos.Responses
{
    public class SessionStatusResponse
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        [JsonProperty("files")]
        public List<FileStatusItem> Files { get; set; }

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

            [JsonProperty("status")]
            public string Status { get; set; }  // backend enum FileStatus → map as string

            [JsonProperty("pendingChunkIndexes")]
            public List<int> PendingChunkIndexes { get; set; }
        }
    }
}
