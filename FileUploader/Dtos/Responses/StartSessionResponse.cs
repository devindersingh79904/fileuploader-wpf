using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileUploader.Dtos.Responses
{
    public class StartSessionResponse
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; }
    }
}
