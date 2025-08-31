using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileUploader.Dtos.Responses
{
    public class RegisterFileResponse
    {
        [JsonProperty("fileId")]
        public string FileId { get; set; }

        [JsonProperty("s3Key")]
        public string S3Key { get; set; }

        [JsonProperty("uploadId")]
        public string UploadId { get; set; }
    }
}
