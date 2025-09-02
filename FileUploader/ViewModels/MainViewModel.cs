using FileUploader.Dtos.Request;
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
        // ===== INotifyPropertyChanged =====
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        // ===== Data =====
        public ObservableCollection<FileRow> Files { get; }

        public SelectFilesCommand SelectFilesCommand { get; }
        public DeleteFileCommand DeleteFileCommand { get; set; }
        public StartUploadCommand StartUploadCommand { get; set; }
        public PauseAllCommand PauseAllCommand { get; }
        public ResumeAllCommand ResumeAllCommand { get; }
        public CancelAllCommand CancelAllCommand { get; }

        private bool _sessionCreated;
        private bool _sessionCompleted;

        private int _expectedFileCount = 0; // NEW: track how many files should complete

        private string _sessionId = string.Empty;
        public string SessionId
        {
            get => _sessionId;
            set { if (_sessionId != value) { _sessionId = value; OnPropertyChanged(nameof(SessionId)); } }
        }

        private string _sessionStatus = string.Empty;
        public string SessionStatus
        {
            get => _sessionStatus;
            set { if (_sessionStatus != value) { _sessionStatus = value; OnPropertyChanged(nameof(SessionStatus)); } }
        }

        private double _overallProgress;
        public double OverallProgress
        {
            get => _overallProgress;
            set { if (Math.Abs(_overallProgress - value) > double.Epsilon) { _overallProgress = value; OnPropertyChanged(nameof(OverallProgress)); } }
        }

        private string _overallProgressText = "Overall: 0 / 0 files uploaded";
        public string OverallProgressText
        {
            get => _overallProgressText;
            set { if (_overallProgressText != value) { _overallProgressText = value; OnPropertyChanged(nameof(OverallProgressText)); } }
        }

        // ===== Services / state =====
        private readonly QueuedUploadService _queued;
        private readonly IUploadManager _uploadManager;
        private readonly IFileUploadApi _api;
        private readonly string _userId = "c001";

        // expose active file so commands can mark it paused immediately
        public string? ActiveFilePath { get; private set; }

        public MainViewModel()
        {
            Files = new ObservableCollection<FileRow>();

            SelectFilesCommand = new SelectFilesCommand(this);
            DeleteFileCommand = new DeleteFileCommand(this);
            StartUploadCommand = new StartUploadCommand(this);
            PauseAllCommand = new PauseAllCommand(this);
            ResumeAllCommand = new ResumeAllCommand(this);
            CancelAllCommand = new CancelAllCommand(this);

            _api = new FileUploadApi();
            var storage = new StorageUploader();
            _uploadManager = new UploadManager(_api, storage);
            _queued = new QueuedUploadService(_uploadManager);

            // Queue -> UI + command state
            _queued.OnQueued += fp => { UpdateRow(fp, "Queued", 0); NotifyCommandStates(); };
            _queued.OnStarted += fp => { ActiveFilePath = fp; UpdateRow(fp, "Uploading", 0); NotifyCommandStates(); };
            _queued.OnProgress += (fp, p) => { UpdateRow(fp, "Uploading", p); };
            _queued.OnCompleted += fp =>
            {
                if (ActiveFilePath == fp) ActiveFilePath = null;
                UpdateRow(fp, "Completed", 100);
                NotifyCommandStates();
                TryCompleteSessionIfDone();
            };
            _queued.OnFailed += (fp, ex) =>
            {
                if (ActiveFilePath == fp) ActiveFilePath = null;
                UpdateRow(fp, $"Failed: {ex.Message}", 0);
                NotifyCommandStates();
            };

            Files.CollectionChanged += OnFilesCollectionChanged;
        }

        // ===== Public ops called by commands =====
        public void StartUploads()
        {
            foreach (var f in Files)
            {
                if (!"Completed".Equals(f.Status, StringComparison.OrdinalIgnoreCase))
                    _ = _queued.EnqueueFileAsync(_userId, f.FullPath, CancellationToken.None);
            }

            _sessionCompleted = false;
            NotifyCommandStates();
        }

        public async Task InitSessionAsync()
        {
            if (_sessionCreated) return;
            _sessionCreated = true;
            try
            {
                SessionStatus = "Creating session...";
                var resp = await _api.StartSessionAsync(new StartSessionRequest { UserId = _userId }, CancellationToken.None);
                SessionId = resp.SessionId;
                SessionStatus = "Session created: " + SessionId;
            }
            catch (Exception ex) { SessionStatus = "Failed: " + ex.Message; }
        }

        public async void PauseAllUploads()
        {
            _queued.PauseAll();
            if (!string.IsNullOrWhiteSpace(SessionId))
            {
                try { await _api.PauseSessionAsync(SessionId, CancellationToken.None); }
                catch (Exception ex) { SessionStatus = "Server pause failed: " + ex.Message; }
            }
            NotifyCommandStates();
        }

        public async void ResumeAllUploads()
        {
            foreach (var row in Files)
            {
                if ("Paused".Equals(row.Status, StringComparison.OrdinalIgnoreCase))
                    row.Status = "Queued";
            }

            if (!string.IsNullOrWhiteSpace(SessionId))
            {
                try { await _api.ResumeSessionAsync(SessionId, CancellationToken.None); }
                catch (Exception ex) { SessionStatus = "Server resume failed: " + ex.Message; }
            }

            _queued.ResumeAll();
            SessionStatus = "Resumed uploads.";
            NotifyCommandStates();
        }

        // ===== Collection / UI helpers =====
        private void OnFilesCollectionChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            NotifyCommandStates();
            RecalcOverallFilesText();
        }

        public void AddFiles(string[] filePaths)
        {
            int start = Files.Count + 1;
            for (int i = 0; i < filePaths.Length; i++)
                Files.Add(FileRow.FromPath(filePaths[i], start + i));

            // Track how many files are expected for this session
            _expectedFileCount = Files.Count;

            NotifyCommandStates();
            RecalcOverallFilesText();
        }

        private void UpdateRow(string filePath, string status, int percent)
        {
            var row = FindRow(filePath);
            if (row != null)
            {
                row.Status = status;
                row.Progress = percent;
            }
            RecalcOverallFilesText();
        }

        public FileRow? FindRow(string filePath)
        {
            foreach (var f in Files)
                if (string.Equals(f.FullPath, filePath, StringComparison.OrdinalIgnoreCase))
                    return f;
            return null;
        }

        private void RecalcOverallFilesText()
        {
            int total = Files?.Count ?? 0;
            int completed = 0;
            if (total > 0)
            {
                foreach (var f in Files)
                    if ("Completed".Equals(f.Status, StringComparison.OrdinalIgnoreCase))
                        completed++;
            }
            OverallProgress = total == 0 ? 0 : (double)completed / total * 100.0;
            OverallProgressText = $"Overall: {completed} / {total} files uploaded";
        }

        private async void TryCompleteSessionIfDone()
        {
            if (_sessionCompleted) return;
            if (string.IsNullOrWhiteSpace(SessionId)) return;

            int completed = 0;
            foreach (var f in Files)
                if ("Completed".Equals(f.Status, StringComparison.OrdinalIgnoreCase))
                    completed++;

            if (completed == _expectedFileCount && _expectedFileCount > 0)
            {
                try
                {
                    await _api.CompleteSessionAsync(SessionId, CancellationToken.None);
                    _sessionCompleted = true;
                    SessionStatus = "Session completed on server.";
                }
                catch (Exception ex)
                {
                    SessionStatus = "Server completion failed: " + ex.Message;
                }
            }
        }

        public void NotifyCommandStates()
        {
            StartUploadCommand?.RaiseCanExecuteChanged();
            PauseAllCommand?.RaiseCanExecuteChanged();
            ResumeAllCommand?.RaiseCanExecuteChanged();
            CancelAllCommand?.RaiseCanExecuteChanged();
        }
    }
}
