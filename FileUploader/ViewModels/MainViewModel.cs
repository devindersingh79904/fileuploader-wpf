using FileUploader.Dtos;
using FileUploader.Dtos.Responses;
using FileUploader.Models;
using FileUploader.ViewModels.Commands;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace FileUploader.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<FileRow> Files { get; }

        public SelectFilesCommand SelectFilesCommand { get; }
        public DeleteFileCommand DeleteFileCommand { get; set; }
        public StartUploadCommand StartUploadCommand { get; set; }
        public PauseAllCommand PauseAllCommand { get; }
        public ResumeAllCommand ResumeAllCommand { get; }
        public CancelAllCommand CancelAllCommand { get; }

        private bool _sessionCreated = false;
        private string _sessionId;
        public string SessionId
        {
            get => _sessionId;
            set { if (_sessionId != value) { _sessionId = value; OnPropertyChanged(nameof(SessionId)); } }
        }

        private string _sessionStatus;
        public string SessionStatus
        {
            get => _sessionStatus;
            set { if (_sessionStatus != value) { _sessionStatus = value; OnPropertyChanged(nameof(SessionStatus)); } }
        }

        private double _overallProgress = 0; // 0..100
        public double OverallProgress
        {
            get => _overallProgress;
            set { if (_overallProgress != value) { _overallProgress = value; OnPropertyChanged(nameof(OverallProgress)); } }
        }

        private string _overallProgressText = "Overall: 0% (0 of 0)";
        public string OverallProgressText
        {
            get => _overallProgressText;
            set { if (_overallProgressText != value) { _overallProgressText = value; OnPropertyChanged(nameof(OverallProgressText)); } }
        }

        // ---- New fields for queued upload orchestration ----
        private readonly QueuedUploadService _queued;
        private readonly IUploadManager _uploadManager;
        private readonly string _userId = "c001"; // static for now, can be bound from UI

        public MainViewModel()
        {
            Files = new ObservableCollection<FileRow>();

            SelectFilesCommand = new SelectFilesCommand(this);
            DeleteFileCommand = new DeleteFileCommand(this);
            StartUploadCommand = new StartUploadCommand(this);
            PauseAllCommand = new PauseAllCommand(this);
            ResumeAllCommand = new ResumeAllCommand(this);
            CancelAllCommand = new CancelAllCommand(this);

            // Build the services
            var api = new FileUploadApi();
            var storage = new StorageUploader();
            _uploadManager = new UploadManager(api, storage);
            _queued = new QueuedUploadService(_uploadManager);

            // Subscribe to queue events
            _queued.OnQueued += fp => UpdateRow(fp, "Queued", 0);
            _queued.OnStarted += fp => UpdateRow(fp, "Uploading", 0);
            _queued.OnProgress += (fp, p) => UpdateRow(fp, "Uploading", p);
            _queued.OnCompleted += fp => UpdateRow(fp, "Completed", 100);
            _queued.OnFailed += (fp, ex) => UpdateRow(fp, $"Failed: {ex.Message}", 0);

            Files.CollectionChanged += OnFilesCollectionChanged;
        }

        // Called by StartUploadCommand
        public void StartUploads()
        {
            foreach (var file in Files)
            {
                // enqueue each file individually
                _ = _queued.EnqueueFileAsync(_userId, file.FullPath, CancellationToken.None);
            }
        }

        public void PauseAllUploads() => _queued.PauseAll();
        public void ResumeAllUploads() => _queued.ResumeAll();

        public async Task InitSessionAsync()
        {
            if (_sessionCreated) return;
            _sessionCreated = true;
            try
            {
                SessionStatus = "Creating session...";
                StartSessionRequest request = new StartSessionRequest { UserId = _userId };

                var token = new CancellationToken();
                StartSessionResponse response = await new FileUploadApi().StartSessionAsync(request, token);

                SessionId = response.SessionId;
                SessionStatus = "Session created: " + SessionId;
            }
            catch (System.Net.Http.HttpRequestException httpEx)
            {
                SessionStatus = "Network error: " + httpEx.Message;
            }
            catch (Exception ex)
            {
                SessionStatus = "Failed: " + ex.Message;
            }
        }

        private void OnFilesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            StartUploadCommand.RaiseCanExecuteChanged();
            PauseAllCommand.RaiseCanExecuteChanged();
            ResumeAllCommand.RaiseCanExecuteChanged();
            CancelAllCommand.RaiseCanExecuteChanged();

            OverallProgressText = $"Overall: {OverallProgress:0}% ({Files.Count} file(s))";
        }

        public void AddFiles(string[] filePaths)
        {
            int start = Files.Count + 1;
            for (int i = 0; i < filePaths.Length; i++)
            {
                Files.Add(FileRow.FromPath(filePaths[i], start + i));
            }
            StartUploadCommand.RaiseCanExecuteChanged();
        }

        // ---- Helpers to update rows ----
        private void UpdateRow(string filePath, string status, int percent)
        {
            var row = FindRow(filePath);
            if (row != null)
            {
                row.Status = status;
                row.Progress = percent;
            }
        }

        private FileRow FindRow(string filePath)
        {
            foreach (var f in Files)
                if (string.Equals(f.FullPath, filePath, StringComparison.OrdinalIgnoreCase))
                    return f;
            return null;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
