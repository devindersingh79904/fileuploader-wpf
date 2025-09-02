using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileUploader.Dtos.Request
{
    public class StartSessionRequest
    {
        [JsonProperty("userId")]
        public string UserId { get; set; }
    }
}
