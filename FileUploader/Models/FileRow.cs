using System.ComponentModel;
using System.IO;

namespace FileUploader.Models
{
    public class FileRow : INotifyPropertyChanged
    {
        public int Index { get; set; }
        public string FileName { get; set; }
        public string FullPath { get; set; }   // <-- used by MainViewModel
        public long SizeBytes { get; set; }

        private int _progress;
        public int Progress
        {
            get => _progress;
            set { if (_progress != value) { _progress = value; OnPropertyChanged(nameof(Progress)); } }
        }

        private string _status = "Ready";
        public string Status
        {
            get => _status;
            set { if (_status != value) { _status = value; OnPropertyChanged(nameof(Status)); } }
        }

        // Optional convenience for UI bindings
        public string SizeText => $"{SizeBytes / 1024d / 1024d:0.##} MB";

        public static FileRow FromPath(string path, int index)
        {
            var fi = new FileInfo(path);
            return new FileRow
            {
                Index = index,
                FileName = fi.Name,
                FullPath = fi.FullName,
                SizeBytes = fi.Exists ? fi.Length : 0,
                Status = "Ready",
                Progress = 0
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
