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
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        public bool CanExecute(object? parameter)
        {
            if (_vm.Files == null || _vm.Files.Count == 0) return false;

            foreach (var row in _vm.Files)
            {
                var s = row.Status ?? "";
                if (s.Equals("Queued", StringComparison.OrdinalIgnoreCase) ||
                    s.Equals("Uploading", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public void Execute(object? parameter)
        {
            if (_vm.Files == null || _vm.Files.Count == 0) return;

            // pause engine + queue
            _vm.PauseAllUploads();

            // mark UI rows
            foreach (FileRow row in _vm.Files)
            {
                if (row.Status.Equals("Queued", StringComparison.OrdinalIgnoreCase) ||
                    row.Status.Equals("Uploading", StringComparison.OrdinalIgnoreCase))
                {
                    row.Status = "Paused";
                }
            }

            _vm.SessionStatus = "All uploads paused.";

            // reevaluate buttons
            RaiseCanExecuteChanged();
            _vm.ResumeAllCommand.RaiseCanExecuteChanged();
            _vm.StartUploadCommand.RaiseCanExecuteChanged();
            _vm.CancelAllCommand.RaiseCanExecuteChanged();
        }
    }
}
