using System.ComponentModel;
using System.IO;

namespace FileUploader.Models
{
    public class FileRow : INotifyPropertyChanged
    {
        public int Index { get; set; }

        // Initialize strings to avoid CS8618 warnings
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        private int _progress;
        public int Progress
        {
            get => _progress;
            set
            {
                if (_progress != value)
                {
                    _progress = value;
                    OnPropertyChanged(nameof(Progress));
                    OnPropertyChanged(nameof(ProgressText));
                }
            }
        }

        private string _status = "Ready";
        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        // Optional error message for UI
        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged(nameof(ErrorMessage));
                }
            }
        }

        // Convenience computed fields
        public string SizeText => $"{SizeBytes / 1024d / 1024d:0.##} MB";
        public string ProgressText => $"{Progress}%";

        public static FileRow FromPath(string path, int index)
        {
            var fi = new FileInfo(path);
            return new FileRow
            {
                Index = index,
                FileName = fi.Exists ? fi.Name : Path.GetFileName(path),
                FullPath = fi.Exists ? fi.FullName : path,
                SizeBytes = fi.Exists ? fi.Length : 0,
                Status = "Ready",
                Progress = 0,
                ErrorMessage = string.Empty
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
} 
