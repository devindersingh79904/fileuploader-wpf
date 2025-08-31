using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileUploader.Dtos.Request
{
    public class CompleteFileRequest
    {
        [JsonProperty("uploadId")]
        public string UploadId { get; set; }

        [JsonProperty("parts")]
        public List<PartETag> Parts { get; set; }

        public class PartETag
        {
            [JsonProperty("partNumber")]
            public int PartNumber { get; set; }

            [JsonProperty("eTag")]
            public string ETag { get; set; }
        }
    }
}
