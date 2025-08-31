using FileUploader.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FileUploader.ViewModels.Commands
{
    public class CancelAllCommand: ICommand
    {
        private readonly MainViewModel _vm;

        public CancelAllCommand(MainViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            // Enabled only if there are files
            return _vm.Files != null && _vm.Files.Count > 0;
        }

        public void Execute(object? parameter)
        {
            if (_vm.Files == null || _vm.Files.Count == 0) return;

            foreach (FileRow row in _vm.Files)
            {
                row.Status = "Canceled";
            }

            _vm.SessionStatus = "All uploads canceled";
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

    }
}
