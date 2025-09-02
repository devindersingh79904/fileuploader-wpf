using FileUploader.ViewModels;
using System;
using System.Windows.Input;

public class StartUploadCommand : ICommand
{
    private readonly MainViewModel _vm;
    private bool _isRunning;

    public StartUploadCommand(MainViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public bool CanExecute(object? parameter)
    {
        if (_isRunning) return false;
        if (_vm.Files == null || _vm.Files.Count == 0) return false;

        // enable only if at least one is not completed
        foreach (var r in _vm.Files)
            if (!"Completed".Equals(r.Status, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    public void Execute(object? parameter)
    {
        if (_vm.Files == null || _vm.Files.Count == 0) return;

        _isRunning = true;
        RaiseCanExecuteChanged(); // disable Start immediately

        // reset non-completed rows to Queued
        foreach (var r in _vm.Files)
        {
            if (!"Completed".Equals(r.Status, StringComparison.OrdinalIgnoreCase))
            {
                r.Status = "Queued";
                r.Progress = 0;
                r.ErrorMessage = string.Empty;
            }
        }

        _vm.SessionStatus = "Starting upload...";
        _vm.OverallProgress = 0;
        _vm.OverallProgressText = $"Overall: 0 / {_vm.Files.Count} files uploaded";

        // IMPORTANT: delegate to VM -> queue (1-at-a-time, pauseable)
        _vm.StartUploads();

        // keep disabled while running; re-enabled by VM when everything completes or new files are added
    }
}
