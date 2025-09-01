using FileUploader.Models;
using FileUploader.ViewModels;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;

public class StartUploadCommand : ICommand
{
    private readonly MainViewModel _vm;

    private readonly IFileUploadApi _api;
    private readonly IStorageUploader _uploader;
    private readonly IUploadManager _manager;

    private CancellationTokenSource _cts;

    // 🔑 New flag to disable Start while running
    private bool _isRunning = false;

    public StartUploadCommand(MainViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));

        _api = new FileUploadApi();
        _uploader = new StorageUploader();
        _manager = new UploadManager(_api, _uploader);

        _cts = new CancellationTokenSource();
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter)
    {
        if (_isRunning) return false;  // ✅ disable once started
        if (_vm.Files == null || _vm.Files.Count == 0) return false;

        foreach (var row in _vm.Files)
        {
            if (!string.Equals(row.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public async void Execute(object? parameter)
    {
        if (_vm.Files == null || _vm.Files.Count == 0)
        {
            MessageBox.Show("Please add at least one file to start upload.", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged(); // disable button immediately

        foreach (var row in _vm.Files)
        {
            if (!string.Equals(row.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                row.Status = "Queued";
                row.Progress = 0;
                row.ErrorMessage = string.Empty;
            }
        }

        _vm.SessionStatus = "Starting upload...";
        _vm.OverallProgress = 0;
        _vm.OverallProgressText = $"Overall: 0 / {_vm.Files.Count} files uploaded";

        try
        {
            string userId = "c001";
            string[] paths = new string[_vm.Files.Count];
            for (int i = 0; i < _vm.Files.Count; i++)
                paths[i] = _vm.Files[i].FullPath;

            string sessionId = await _manager.StartUploadAsync(
                userId,
                paths,
                (path, percent) => OnProgress(path, percent),
                _cts.Token
            );

            _vm.SessionId = sessionId;
            _vm.SessionStatus = "Upload finished. Session: " + sessionId;
        }
        catch (OperationCanceledException)
        {
            _vm.SessionStatus = "Upload canceled.";
        }
        catch (Exception ex)
        {
            _vm.SessionStatus = "Upload failed: " + ex.Message;
        }

        // ✅ mark as finished, re-enable only if files are not all completed
        _isRunning = false;
        RaiseCanExecuteChanged();
    }

    private void OnProgress(string path, int percent)
    {
        var row = FindRowByPath(path);
        if (row != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                row.Progress = percent;
                row.Status = percent >= 100 ? "Completed" : "Uploading";

                int completed = 0;
                foreach (var f in _vm.Files)
                    if (string.Equals(f.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                        completed++;

                _vm.OverallProgress = (double)completed / _vm.Files.Count * 100.0;
                _vm.OverallProgressText = $"Overall: {completed} / {_vm.Files.Count} files uploaded";

                RaiseCanExecuteChanged(); // keep button state in sync
            });
        }
    }

    private FileRow? FindRowByPath(string path)
    {
        if (_vm.Files == null) return null;
        foreach (var row in _vm.Files)
        {
            if (string.Equals(row.FullPath, path, StringComparison.OrdinalIgnoreCase))
                return row;
        }
        return null;
    }
}
