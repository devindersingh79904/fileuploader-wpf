using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FileUploader.LocalState
{
    public class UploadStateStore
    {
        private readonly string _filePath;

        public UploadStateStore(string filePath = "upload_state.json")
        {
            _filePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, filePath);
        }

        public Dictionary<string, UploadStateDto> Load()
        {
            if (!File.Exists(_filePath)) return new();
            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<Dictionary<string, UploadStateDto>>(json) ?? new();
            }
            catch
            {
                return new();
            }
        }

        public void Save(Dictionary<string, UploadStateDto> dict)
        {
            var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }

        internal void Clear()
        {
            throw new NotImplementedException();
        }
    }
}
