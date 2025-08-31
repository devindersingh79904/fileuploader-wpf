using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace FileUploader.Models
{
    public class FileRow: INotifyPropertyChanged
    {
        public int Index { get; set; }
        public string FileName { get; set; }
        public string SizeHuman { get; set; }
        public string FilePath { get; set; }

        private string _status;
        private double _progress;
        private string _errorMessage;

        public string Status
        {
            get { return _status; }
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public double Progress
        {
            get { return _progress; }
            set
            {
                if (Math.Abs(_progress - value) > 0.001)
                {
                    _progress = value;
                    OnPropertyChanged(nameof(Progress));
                }
            }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged(nameof(ErrorMessage));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public static FileRow FromPath(string path, int index)
        {
            var info = new FileInfo(path);

            return new FileRow
            {
                Index = index,
                FilePath = path,
                FileName = Path.GetFileName(path),
                SizeHuman = FormatSize(info.Length),

                // Initial default values
                Status = "Pending",
                Progress = 0,
                ErrorMessage = string.Empty
            };
        }

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes; int i = 0;
            while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
            return $"{size:0.##} {units[i]}";
        }
    }
}
