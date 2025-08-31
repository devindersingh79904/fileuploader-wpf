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

        // Orchestration
        private readonly QueuedUploadService _queued;
        private readonly IUploadManager _uploadManager;
        private readonly string _userId = "c001";

        // Tracks which file was actively uploading when pause was hit
        private string _activeFilePath;

        public MainViewModel()
        {
            Files = new ObservableCollection<FileRow>();

            SelectFilesCommand = new SelectFilesCommand(this);
            DeleteFileCommand = new DeleteFileCommand(this);
            StartUploadCommand = new StartUploadCommand(this);
            PauseAllCommand = new PauseAllCommand(this);
            ResumeAllCommand = new ResumeAllCommand(this);
            CancelAllCommand = new CancelAllCommand(this);

            var api = new FileUploadApi();
            var storage = new StorageUploader();
            _uploadManager = new UploadManager(api, storage);
            _queued = new QueuedUploadService(_uploadManager);

            // Queue events -> update rows + track active file + notify command states
            _queued.OnQueued += fp => { UpdateRow(fp, "Queued", 0); NotifyCommandStates(); };
            _queued.OnStarted += fp => { _activeFilePath = fp; UpdateRow(fp, "Uploading", 0); NotifyCommandStates(); };
            _queued.OnProgress += (fp, p) => { UpdateRow(fp, "Uploading", p);    /* no spam requery */ };
            _queued.OnCompleted += fp => { if (_activeFilePath == fp) _activeFilePath = null; UpdateRow(fp, "Completed", 100); NotifyCommandStates(); };
            _queued.OnFailed += (fp, ex) => { if (_activeFilePath == fp) _activeFilePath = null; UpdateRow(fp, $"Failed: {ex.Message}", 0); NotifyCommandStates(); };

            Files.CollectionChanged += OnFilesCollectionChanged;
        }

        // Called by StartUploadCommand
        public void StartUploads()
        {
            foreach (var file in Files)
            {
                _ = _queued.EnqueueFileAsync(_userId, file.FullPath, CancellationToken.None);
            }
            NotifyCommandStates();
        }

        public void PauseAllUploads()
        {
            _queued.PauseAll();   // freezes queue + cancels in-flight chunk
            // UI rows changed by PauseAllCommand; just requery commands here
            NotifyCommandStates();
        }

        public void ResumeAllUploads()
        {
            // 1) Unfreeze queue so any pre-existing queued jobs continue in original order
            _queued.ResumeAll();

            // 2) Flip UI from Paused -> Queued (UI consistency only; do NOT enqueue all)
            foreach (var row in Files)
            {
                if (string.Equals(row.Status, "Paused", StringComparison.OrdinalIgnoreCase))
                    row.Status = "Queued";
            }

            // 3) Only re-enqueue the file that was actively uploading when pause happened.
            //    (All other paused files were already in the queue; re-enqueuing causes duplicates.)
            if (!string.IsNullOrWhiteSpace(_activeFilePath))
            {
                var row = FindRow(_activeFilePath);
                if (row != null)
                {
                    // Keep UI as Queued; the queue will raise OnStarted when it actually begins
                    _ = _queued.EnqueueFileAsync(_userId, row.FullPath, CancellationToken.None);
                }
                // Clear the marker; next OnStarted will set it again
                _activeFilePath = null;
            }

            SessionStatus = "Resumed uploads.";
            NotifyCommandStates();
        }

        public async Task InitSessionAsync()
        {
            if (_sessionCreated) return;
            _sessionCreated = true;
            try
            {
                SessionStatus = "Creating session...";
                var request = new StartSessionRequest { UserId = _userId };
                var response = await new FileUploadApi().StartSessionAsync(request, CancellationToken.None);

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
            NotifyCommandStates();
            OverallProgressText = $"Overall: {OverallProgress:0}% ({Files.Count} file(s))";
        }

        public void AddFiles(string[] filePaths)
        {
            int start = Files.Count + 1;
            for (int i = 0; i < filePaths.Length; i++)
                Files.Add(FileRow.FromPath(filePaths[i], start + i));

            NotifyCommandStates();
        }

        // ---- Helpers ----
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

        public void NotifyCommandStates()
        {
            StartUploadCommand?.RaiseCanExecuteChanged();
            PauseAllCommand?.RaiseCanExecuteChanged();
            ResumeAllCommand?.RaiseCanExecuteChanged();
            CancelAllCommand?.RaiseCanExecuteChanged();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
