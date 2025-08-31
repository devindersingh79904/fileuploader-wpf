using FileUploader.Models;
using FileUploader.ViewModels;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;

public class StartUploadCommand : ICommand
{
    private readonly MainViewModel _vm;
    private CancellationTokenSource _cts;

    public StartUploadCommand(MainViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        _cts = new CancellationTokenSource();
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _vm.Files != null && _vm.Files.Count > 0;
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Execute(object? parameter)
    {
        if (_vm.Files == null || _vm.Files.Count == 0)
        {
            MessageBox.Show("Please add at least one file to start upload.", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Reset rows to a known initial state
        for (int i = 0; i < _vm.Files.Count; i++)
        {
            var row = _vm.Files[i];
            row.Status = "Queued";
            row.Progress = 0;

            // Clear ErrorMessage if your FileRow has it
            try
            {
                var errProp = row.GetType().GetProperty("ErrorMessage");
                if (errProp != null && errProp.CanWrite) errProp.SetValue(row, string.Empty);
            }
            catch { /* ignore if not present */ }
        }

        _vm.SessionStatus = "Queued files. Uploads will run one-by-one…";
        _vm.OverallProgress = 0;
        _vm.OverallProgressText = $"Overall: 0% ({_vm.Files.Count} file(s))";

        // Hand off to the ViewModel's queued flow
        // (This enqueues each file and processes them FIFO; UI is updated via queue events.)
        try
        {
            _vm.StartUploads();
        }
        catch (OperationCanceledException)
        {
            _vm.SessionStatus = "Upload canceled.";
        }
        catch (Exception ex)
        {
            _vm.SessionStatus = "Upload failed to start: " + ex.Message;

            // Mark first incomplete row as failed (defensive)
            for (int i = 0; i < _vm.Files.Count; i++)
            {
                if (_vm.Files[i].Progress < 100)
                {
                    _vm.Files[i].Status = "Failed";
                    try
                    {
                        var errProp = _vm.Files[i].GetType().GetProperty("ErrorMessage");
                        if (errProp != null && errProp.CanWrite) errProp.SetValue(_vm.Files[i], ex.Message);
                    }
                    catch { }
                    break;
                }
            }
        }
    }
}
