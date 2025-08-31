using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileUploader.Dtos.Request
{
    public class PresignPartUrlRequest
    {
        [JsonProperty("partNumber")]
        public int PartNumber { get; set; }
    }
}
