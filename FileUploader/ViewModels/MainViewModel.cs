using FileUploader.Dtos;
using FileUploader.Dtos.Request;
using FileUploader.Dtos.Responses;
using FileUploader.Models;
using FileUploader.ViewModels.Commands;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;              // Dispatcher
using System.Windows.Media;        // Brushes

namespace FileUploader.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // ===== INotifyPropertyChanged =====
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        // ===== Simple UI dispatcher helper =====
        private static void RunOnUi(Action action)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) action();
            else d.Invoke(action);
        }

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

        // Optional: if you show colored status text in the view
        private Brush _sessionStatusBrush = Brushes.Black;
        public Brush SessionStatusBrush
        {
            get => _sessionStatusBrush;
            set { if (_sessionStatusBrush != value) { _sessionStatusBrush = value; OnPropertyChanged(nameof(SessionStatusBrush)); } }
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
            _queued.OnQueued += fp =>
            {
                RunOnUi(() =>
                {
                    var row = FindRow(fp);
                    var keepPercent = row?.Progress ?? 0;   // keep previous percent
                    UpdateRow(fp, "Queued", keepPercent);
                    NotifyCommandStates();
                });
            };

            _queued.OnStarted += fp =>
            {
                RunOnUi(() =>
                {
                    ActiveFilePath = fp;
                    var row = FindRow(fp);
                    var keepPercent = row?.Progress ?? 0;   // keep previous percent
                    UpdateRow(fp, "Uploading", keepPercent);
                    NotifyCommandStates();
                });
            };

            _queued.OnProgress += (fp, p) =>
            {
                RunOnUi(() => { UpdateRow(fp, "Uploading", p); });
            };

            _queued.OnCompleted += fp =>
            {
                RunOnUi(() =>
                {
                    if (ActiveFilePath == fp) ActiveFilePath = null;
                    UpdateRow(fp, "Completed", 100);
                    NotifyCommandStates();
                    TryCompleteSessionIfDone();
                });
            };

            _queued.OnFailed += (fp, ex) =>
            {
                RunOnUi(() =>
                {
                    if (ActiveFilePath == fp) ActiveFilePath = null;
                    UpdateRow(fp, $"Failed: {ex.Message}", 0);
                    SessionStatus = $"Error: {ex.Message}";
                    SessionStatusBrush = Brushes.Red;
                    NotifyCommandStates();
                });
            };

            Files.CollectionChanged += OnFilesCollectionChanged;
        }

        // ===== Public ops called by commands =====
        public void StartUploads()
        {
            // Let queue events drive the UI. Only enqueue items not completed or already uploading.
            foreach (var f in Files)
            {
                if (!"Completed".Equals(f.Status, StringComparison.OrdinalIgnoreCase) &&
                    !"Uploading".Equals(f.Status, StringComparison.OrdinalIgnoreCase))
                {
                    _ = _queued.EnqueueFileAsync(_userId, f.FullPath, CancellationToken.None);
                }
            }

            _sessionCompleted = false;
            NotifyCommandStates();
        }

        public async Task InitSessionAsync()
        {
            if (_sessionCreated) return;
            _sessionCreated = true;

            RunOnUi(() =>
            {
                SessionStatus = "Creating session...";
                SessionStatusBrush = Brushes.Black;
            });

            try
            {
                var resp = await _api.StartSessionAsync(new StartSessionRequest { UserId = _userId }, CancellationToken.None);
                RunOnUi(() =>
                {
                    SessionId = resp.SessionId;
                    SessionStatus = "Session created: " + SessionId;  // keep this line
                    SessionStatusBrush = Brushes.Green;
                });
            }
            catch (Exception ex)
            {
                RunOnUi(() =>
                {
                    SessionStatus = "Failed: " + ex.Message;
                    SessionStatusBrush = Brushes.Red;
                });
            }
        }

        public async void PauseAllUploads()
        {
            _queued.PauseAll();

            if (!string.IsNullOrWhiteSpace(SessionId))
            {
                try
                {
                    await _api.PauseSessionAsync(SessionId, CancellationToken.None);
                    RunOnUi(() =>
                    {
                        SessionStatus = "All uploads paused.";
                        SessionStatusBrush = Brushes.Black;
                    });
                }
                catch (Exception ex)
                {
                    RunOnUi(() =>
                    {
                        SessionStatus = "Server pause failed: " + ex.Message;
                        SessionStatusBrush = Brushes.Red;
                    });
                }
            }

            // Mark UI rows that are currently queued/uploading as Paused (keep %)
            RunOnUi(() =>
            {
                foreach (FileRow row in Files)
                {
                    if (row.Status.Equals("Queued", StringComparison.OrdinalIgnoreCase) ||
                        row.Status.Equals("Uploading", StringComparison.OrdinalIgnoreCase))
                    {
                        row.Status = "Paused";   // do NOT reset row.Progress
                    }
                }
                NotifyCommandStates();
            });
        }

        public async void ResumeAllUploads()
        {
            // Flip only Paused -> Queued in UI (progress stays as-is)
            RunOnUi(() =>
            {
                foreach (var row in Files)
                {
                    if ("Paused".Equals(row.Status, StringComparison.OrdinalIgnoreCase))
                        row.Status = "Queued";
                }
            });

            if (!string.IsNullOrWhiteSpace(SessionId))
            {
                try
                {
                    await _api.ResumeSessionAsync(SessionId, CancellationToken.None);
                    RunOnUi(() =>
                    {
                        SessionStatus = "Resumed uploads.";
                        SessionStatusBrush = Brushes.Black;
                    });
                }
                catch (Exception ex)
                {
                    RunOnUi(() =>
                    {
                        SessionStatus = "Server resume failed: " + ex.Message;
                        SessionStatusBrush = Brushes.Red;
                    });
                }
            }

            _queued.ResumeAll();
            RunOnUi(NotifyCommandStates);
        }

        // ===== Collection / UI helpers =====
        private void OnFilesCollectionChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RunOnUi(() =>
            {
                NotifyCommandStates();
                RecalcOverallFilesText();
            });
        }

        public void AddFiles(string[] filePaths)
        {
            RunOnUi(() =>
            {
                int start = Files.Count + 1;
                for (int i = 0; i < filePaths.Length; i++)
                    Files.Add(FileRow.FromPath(filePaths[i], start + i));

                NotifyCommandStates();
                RecalcOverallFilesText();
            });
        }

        private void UpdateRow(string filePath, string status, int percent)
        {
            RunOnUi(() =>
            {
                var row = FindRow(filePath);
                if (row != null)
                {
                    row.Status = status;
                    row.Progress = percent;
                }
                RecalcOverallFilesText();
            });
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

            int total = Files?.Count ?? 0;
            if (total == 0) return;

            int completed = 0;
            foreach (var f in Files)
                if ("Completed".Equals(f.Status, StringComparison.OrdinalIgnoreCase))
                    completed++;

            if (completed == total)
            {
                try
                {
                    await _api.CompleteSessionAsync(SessionId, CancellationToken.None);
                    _sessionCompleted = true;
                    RunOnUi(() =>
                    {
                        SessionStatus = "Session completed on server.";
                        SessionStatusBrush = Brushes.Green;
                    });
                }
                catch (Exception ex)
                {
                    RunOnUi(() =>
                    {
                        SessionStatus = "Server completion failed: " + ex.Message;
                        SessionStatusBrush = Brushes.Red;
                    });
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
