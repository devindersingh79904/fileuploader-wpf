using FileUploader.Models;
using FileUploader.ViewModels;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;

// These are from your project (you already have them):
// IFileUploadApi, FileUploadApi, IStorageUploader, StorageUploader, UploadManager

public class StartUploadCommand : ICommand
{
    private readonly MainViewModel _vm;

    // upload helpers
    private readonly IFileUploadApi _api;
    private readonly IStorageUploader _uploader;
    private readonly IUploadManager _manager;

    private CancellationTokenSource _cts;

    public StartUploadCommand(MainViewModel vm)
    {
        if (vm == null) throw new ArgumentNullException("vm");
        _vm = vm;

        // concrete implementations you already wrote
        _api = new FileUploadApi();
        _uploader = new StorageUploader();
        _manager = new UploadManager(_api, _uploader);

        _cts = new CancellationTokenSource();
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _vm.Files != null && _vm.Files.Count > 0;
    }

    public async void Execute(object? parameter)
    {
        if (_vm.Files == null || _vm.Files.Count == 0)
        {
            MessageBox.Show("Please add at least one file to start upload.", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // reset UI rows
        int i = 0;
        while (i < _vm.Files.Count)
        {
            FileRow row = _vm.Files[i];
            row.Status = "Queued";
            row.Progress = 0;
            row.ErrorMessage = string.Empty;
            i = i + 1;
        }

        // make the array of file paths
        string[] paths = new string[_vm.Files.Count];
        int j = 0;
        while (j < _vm.Files.Count)
        {
            paths[j] = _vm.Files[j].FilePath;
            j = j + 1;
        }

        _vm.SessionStatus = "Starting upload...";
        _vm.OverallProgress = 0;
        _vm.OverallProgressText = "Overall: 0% (" + _vm.Files.Count + " files)";

        try
        {
            // user id fixed as "c001" (as per your requirement)
            string userId = "c001";

            // Start the upload; this will also create the session on the server
            // and return the sessionId.
            string sessionId = await _manager.StartUploadAsync(
                userId,
                paths,
                new Action<string, int>(OnProgress),    // progress callback
                _cts.Token
            );

            // store sessionId in the VM and show success
            _vm.SessionId = sessionId;
            _vm.SessionStatus = "Upload finished. Session: " + sessionId;

            // ensure any file stuck <100% is marked appropriately
            int k = 0;
            while (k < _vm.Files.Count)
            {
                if (_vm.Files[k].Progress >= 100)
                {
                    _vm.Files[k].Status = "Uploaded";
                }
                else if (_vm.Files[k].Status == "Queued")
                {
                    _vm.Files[k].Status = "Failed";
                }
                k = k + 1;
            }

            // finalize overall text
            _vm.OverallProgress = CalculateOverallPercent();
            _vm.OverallProgressText = "Overall: " + _vm.OverallProgress.ToString("0")
                                      + "% (" + _vm.Files.Count + " file(s))";
        }
        catch (OperationCanceledException)
        {
            _vm.SessionStatus = "Upload canceled.";
        }
        catch (Exception ex)
        {
            _vm.SessionStatus = "Upload failed: " + ex.Message;

            // mark the first incomplete file with the error
            int idx = 0;
            while (idx < _vm.Files.Count)
            {
                if (_vm.Files[idx].Progress < 100)
                {
                    _vm.Files[idx].Status = "Failed";
                    _vm.Files[idx].ErrorMessage = ex.Message;
                    break;
                }
                idx = idx + 1;
            }
        }
    }

    // ===== Helpers =====

    // This method is called by UploadManager for each file chunk progress.
    // It receives the file path and the percentage.
    private void OnProgress(string path, int percent)
    {
        // find the row by path
        FileRow row = FindRowByPath(path);
        if (row != null)
        {
            // Update per-file progress and status on the UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                row.Progress = percent;
                if (percent >= 100)
                {
                    row.Status = "Uploaded";
                }
                else
                {
                    row.Status = "Uploading";
                }

                _vm.OverallProgress = CalculateOverallPercent();
                _vm.OverallProgressText = "Overall: " + _vm.OverallProgress.ToString("0")
                                          + "% (" + _vm.Files.Count + " file(s))";
            });
        }
    }

    private FileRow FindRowByPath(string path)
    {
        if (_vm.Files == null) return null;
        int i = 0;
        while (i < _vm.Files.Count)
        {
            if (string.Equals(_vm.Files[i].FilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return _vm.Files[i];
            }
            i = i + 1;
        }
        return null;
    }

    private double CalculateOverallPercent()
    {
        if (_vm.Files == null) return 0.0;
        if (_vm.Files.Count == 0) return 0.0;

        double sum = 0.0;
        int i = 0;
        while (i < _vm.Files.Count)
        {
            sum = sum + _vm.Files[i].Progress;
            i = i + 1;
        }
        return sum / _vm.Files.Count;
    }

    public void RaiseCanExecuteChanged()
    {
        if (CanExecuteChanged != null)
        {
            CanExecuteChanged(this, EventArgs.Empty);
        }
    }
}
