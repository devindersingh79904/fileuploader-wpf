using FileUploader.Models;
using System;
using System.Windows.Input;

namespace FileUploader.ViewModels.Commands
{
    public class PauseAllCommand : ICommand
    {
        private readonly MainViewModel _vm;

        public PauseAllCommand(MainViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            if (_vm.Files == null || _vm.Files.Count == 0) return false;

            // Allow pause when anything is actively uploading or waiting in queue
            foreach (var row in _vm.Files)
            {
                if (string.Equals(row.Status, "Uploading", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(row.Status, "Queued", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void Execute(object? parameter)
        {
            if (_vm.Files == null || _vm.Files.Count == 0) return;

            // 1) pause engine + queue
            _vm.PauseAllUploads();

            // 2) mark UI rows
            foreach (FileRow row in _vm.Files)
            {
                if (row.Status.Equals("Uploading", StringComparison.OrdinalIgnoreCase) ||
                    row.Status.Equals("Queued", StringComparison.OrdinalIgnoreCase))
                {
                    row.Status = "Paused";
                }
            }

            _vm.SessionStatus = "All uploads paused.";

            // 3) notify commands to re-check CanExecute
            RaiseCanExecuteChanged();                 // this command
            _vm.ResumeAllCommand.RaiseCanExecuteChanged(); // resume becomes enabled now
            _vm.StartUploadCommand?.RaiseCanExecuteChanged();
            _vm.CancelAllCommand?.RaiseCanExecuteChanged();
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
