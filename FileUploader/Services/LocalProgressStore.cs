using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileUploader.Services
{
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.IO;

    public class LocalProgressStore
    {
        private readonly string _filePath;

        public LocalProgressStore(string? customPath = null)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FileUploader");
            Directory.CreateDirectory(dir);
            _filePath = customPath ?? Path.Combine(dir, "progress.json");
        }

        // What we persist per local file
        public class Entry
        {
            public string SessionId { get; set; } = "";
            public string FileId { get; set; } = "";
            public string UploadId { get; set; } = "";
            public int ChunkCount { get; set; }

            // Saved parts (so we can resume later)
            public List<Part> Parts { get; set; } = new();

            public class Part
            {
                public int PartNumber { get; set; }
                public string ETag { get; set; } = "";
            }
        }

        private class Root
        {
            public Dictionary<string, Entry> Map { get; set; } = new(); // key = local file path
        }

        private Root Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return new Root();
                var json = File.ReadAllText(_filePath);
                return JsonConvert.DeserializeObject<Root>(json) ?? new Root();
            }
            catch { return new Root(); }
        }

        private void Save(Root r)
        {
            var json = JsonConvert.SerializeObject(r, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }

        public Entry? Get(string localPath)
        {
            var r = Load();
            return r.Map.TryGetValue(localPath, out var e) ? e : null;
        }

        public void Upsert(string localPath, Entry e)
        {
            var r = Load();
            r.Map[localPath] = e;
            Save(r);
        }

        public void Remove(string localPath)
        {
            var r = Load();
            if (r.Map.Remove(localPath))
                Save(r);
        }
    }
}
