using System;
using System.Windows.Input;

namespace FileUploader.ViewModels.Commands
{
    public class ResumeAllCommand : ICommand
    {
        private readonly MainViewModel _vm;

        public ResumeAllCommand(MainViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            if (_vm.Files == null || _vm.Files.Count == 0) return false;

            // Enable only if at least one file is paused
            foreach (var row in _vm.Files)
            {
                if (string.Equals(row.Status, "Paused", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void Execute(object? parameter)
        {
            if (_vm.Files == null || _vm.Files.Count == 0) return;

            // Delegates to VM:
            // - unfreezes the queue
            // - sets Paused -> Queued
            // - re-enqueues paused items
            // - UploadManager resumes from saved chunk state
            _vm.ResumeAllUploads();

            _vm.SessionStatus = "Resumed paused uploads.";

            // Commands reevaluate now that state changed
            RaiseCanExecuteChanged();                   // this command (likely disables if nothing remains paused)
            _vm.PauseAllCommand.RaiseCanExecuteChanged(); // pause may be available again
            _vm.StartUploadCommand?.RaiseCanExecuteChanged();
            _vm.CancelAllCommand?.RaiseCanExecuteChanged();
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
