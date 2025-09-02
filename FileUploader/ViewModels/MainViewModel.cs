// File: ViewModels/MainViewModel.cs
using FileUploader.Dtos.Request;
using FileUploader.Models;
using FileUploader.ViewModels.Commands;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using FileUploader.LocalState;


namespace FileUploader.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        private static void UI(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) a(); else d.Invoke(a);
        }

        public ObservableCollection<FileRow> Files { get; } = new();

        public SelectFilesCommand SelectFilesCommand { get; }
        public DeleteFileCommand DeleteFileCommand { get; }
        public StartUploadCommand StartUploadCommand { get; }
        public PauseAllCommand PauseAllCommand { get; }
        public ResumeAllCommand ResumeAllCommand { get; }
        public CancelAllCommand CancelAllCommand { get; }

        private string _sessionId = string.Empty;
        public string SessionId { get => _sessionId; set { if (_sessionId != value) { _sessionId = value; OnPropertyChanged(nameof(SessionId)); } } }

        private string _sessionStatus = "";
        public string SessionStatus { get => _sessionStatus; set { if (_sessionStatus != value) { _sessionStatus = value; OnPropertyChanged(nameof(SessionStatus)); } } }

        private Brush _sessionStatusBrush = Brushes.Black;
        public Brush SessionStatusBrush { get => _sessionStatusBrush; set { if (_sessionStatusBrush != value) { _sessionStatusBrush = value; OnPropertyChanged(nameof(SessionStatusBrush)); } } }

        private double _overallProgress;
        public double OverallProgress { get => _overallProgress; set { if (Math.Abs(_overallProgress - value) > double.Epsilon) { _overallProgress = value; OnPropertyChanged(nameof(OverallProgress)); } } }

        private string _overallProgressText = "Overall: 0 / 0 files uploaded";
        public string OverallProgressText { get => _overallProgressText; set { if (_overallProgressText != value) { _overallProgressText = value; OnPropertyChanged(nameof(OverallProgressText)); } } }

        private readonly QueuedUploadService _queued;
        private readonly IUploadManager _uploadManager;
        private readonly IFileUploadApi _api;
        private readonly LocalState.UploadStateStore _store;

        private bool _sessionCreated;
        private bool _sessionCompleted;

        private readonly string _userId = "c001";
        public string? ActiveFilePath { get; private set; }

        public MainViewModel()
        {
            SelectFilesCommand = new SelectFilesCommand(this);
            DeleteFileCommand = new DeleteFileCommand(this);
            StartUploadCommand = new StartUploadCommand(this);
            PauseAllCommand = new PauseAllCommand(this);
            ResumeAllCommand = new ResumeAllCommand(this);
            CancelAllCommand = new CancelAllCommand(this);

            _api = new FileUploadApi();
            _store = new LocalState.UploadStateStore();   // concrete store used by UploadManager
            var storage = new StorageUploader();
            _uploadManager = new UploadManager(_api, storage, _store);
            _queued = new QueuedUploadService(_uploadManager);

            // queue -> UI
            _queued.OnQueued += fp => UI(() =>
            {
                // keep existing %; only flip status
                var row = FindRow(fp);
                if (row != null && !row.Status.Equals("Paused", StringComparison.OrdinalIgnoreCase))
                    row.Status = "Queued";
                else if (row != null && row.Status.Equals("Paused", StringComparison.OrdinalIgnoreCase))
                    row.Status = "Queued"; // keep percent
                else
                    UpdateRow(fp, "Queued", Preserve: true);

                NotifyCommandStates();
            });

            _queued.OnStarted += fp => UI(() =>
            {
                ActiveFilePath = fp;
                UpdateRow(fp, "Uploading", Preserve: true);
                NotifyCommandStates();
            });

            _queued.OnProgress += (fp, p) => UI(() => UpdateRow(fp, "Uploading", p));

            _queued.OnCompleted += fp => UI(() =>
            {
                if (ActiveFilePath == fp) ActiveFilePath = null;
                UpdateRow(fp, "Completed", 100);
                NotifyCommandStates();
                TryCompleteSessionIfDone();
            });

            _queued.OnFailed += (fp, ex) => UI(() =>
            {
                if (ActiveFilePath == fp) ActiveFilePath = null;
                UpdateRow(fp, $"Failed: {ex.Message}", Preserve: true);
                SessionStatus = $"Error: {ex.Message}";
                SessionStatusBrush = Brushes.Red;
                NotifyCommandStates();
            });

            Files.CollectionChanged += (_, __) => UI(() => { NotifyCommandStates(); RecalcOverallText(); });

            // Optional: hydrate any leftover state (will keep progress)
            HydrateFromStore();
        }

        private void HydrateFromStore()
        {
            var dict = _store.Load();
            if (dict.Count == 0) return;

            foreach (var kv in dict)
            {
                var row = FileRow.FromPath(kv.Key, Files.Count + 1);
                row.Progress = kv.Value.ProgressPercent;
                row.Status = string.IsNullOrWhiteSpace(kv.Value.FileId) ? "Queued" : "Paused"; // “paused/incomplete” visual
                Files.Add(row);
            }
            RecalcOverallText();
        }

        public async Task InitSessionAsync()
        {
            if (_sessionCreated) return;
            _sessionCreated = true;

            UI(() => { SessionStatus = "Creating session..."; SessionStatusBrush = Brushes.Black; });

            try
            {
                var resp = await _api.StartSessionAsync(new StartSessionRequest { UserId = _userId }, CancellationToken.None);
                UI(() =>
                {
                    SessionId = resp.SessionId;
                    SessionStatus = "Session created: " + SessionId;   // keep visible
                    SessionStatusBrush = Brushes.Green;
                });
            }
            catch (Exception ex)
            {
                UI(() => { SessionStatus = "Failed: " + ex.Message; SessionStatusBrush = Brushes.Red; });
            }
        }

        public void StartUploads()
        {
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

        public async void PauseAllUploads()
        {
            _queued.PauseAll();

            if (!string.IsNullOrWhiteSpace(SessionId))
            {
                try
                {
                    await _api.PauseSessionAsync(SessionId, CancellationToken.None);
                    UI(() => { SessionStatus = "All uploads paused."; SessionStatusBrush = Brushes.Black; });
                }
                catch (Exception ex)
                {
                    UI(() => { SessionStatus = "Server pause failed: " + ex.Message; SessionStatusBrush = Brushes.Red; });
                }
            }

            UI(() =>
            {
                foreach (var row in Files)
                {
                    if (row.Status.Equals("Queued", StringComparison.OrdinalIgnoreCase) ||
                        row.Status.Equals("Uploading", StringComparison.OrdinalIgnoreCase))
                    {
                        row.Status = "Paused"; // DO NOT reset Progress
                    }
                }
                NotifyCommandStates();
            });
        }

        public async void ResumeAllUploads()
        {
            if (Files.Count == 0) return;

            RunOnUi(() =>
            {
                foreach (var row in Files)
                {
                    if ("Paused".Equals(row.Status, StringComparison.OrdinalIgnoreCase))
                    {
                        row.Status = "Uploading"; // show actively uploading
                                                  // Enqueue the file again (reuses persisted FileId/UploadId)
                        _ = _queued.EnqueueFileAsync(_userId, row.FullPath, CancellationToken.None);
                    }
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

            _queued.ResumeAll(); // just in case queue had tasks paused in same run
            RunOnUi(NotifyCommandStates);
        }


        public void AddFiles(string[] filePaths)
        {
            UI(() =>
            {
                int start = Files.Count + 1;
                foreach (var p in filePaths)
                {
                    var row = FileRow.FromPath(p, start++);
                    row.Status = "Queued";
                    Files.Add(row);
                }
                RecalcOverallText();
                NotifyCommandStates();
            });
        }

        private FileRow? FindRow(string filePath)
        {
            foreach (var f in Files)
                if (string.Equals(f.FullPath, filePath, StringComparison.OrdinalIgnoreCase))
                    return f;
            return null;
        }

        private void UpdateRow(string filePath, string status, int? percent = null, bool Preserve = false)
        {
            var row = FindRow(filePath);
            if (row == null) return;

            if (!Preserve) row.Status = status;
            else
            {
                // keep existing percent; just flip status
                row.Status = status;
            }

            if (percent.HasValue) row.Progress = percent.Value;

            RecalcOverallText();
        }

        private void RecalcOverallText()
        {
            int total = Files.Count, done = 0;
            foreach (var f in Files)
                if (f.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)) done++;
            OverallProgress = total == 0 ? 0 : (double)done / total * 100.0;
            OverallProgressText = $"Overall: {done} / {total} files uploaded";
        }

        private async void TryCompleteSessionIfDone()
        {
            if (_sessionCompleted || string.IsNullOrWhiteSpace(SessionId)) return;

            int total = Files.Count, done = 0;
            foreach (var f in Files)
                if (f.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)) done++;

            if (total > 0 && done == total)
            {
                try
                {
                    await _api.CompleteSessionAsync(SessionId, CancellationToken.None);
                    _sessionCompleted = true;

                    // clear persisted local store if you want a clean slate:
                    try { _store.Clear(); } catch { /* ignore */ }

                    UI(() => { SessionStatus = "Session completed on server."; SessionStatusBrush = Brushes.Green; });
                }
                catch (Exception ex)
                {
                    UI(() => { SessionStatus = "Server completion failed: " + ex.Message; SessionStatusBrush = Brushes.Red; });
                }
            }
        }

        public void NotifyCommandStates()
        {
            StartUploadCommand.RaiseCanExecuteChanged();
            PauseAllCommand.RaiseCanExecuteChanged();
            ResumeAllCommand.RaiseCanExecuteChanged();
            CancelAllCommand.RaiseCanExecuteChanged();
        }
        // Add this private helper method to MainViewModel to fix CS0103
        private static void RunOnUi(Action action)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) action();
            else d.Invoke(action);
        }
    }
}
