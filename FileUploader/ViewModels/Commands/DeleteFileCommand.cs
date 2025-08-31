using FileUploader.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FileUploader.ViewModels.Commands
{
    public class DeleteFileCommand: ICommand
    {

        private readonly MainViewModel _vm;

        public DeleteFileCommand(MainViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            if (parameter == null) return false;
            var row = parameter as FileRow;
            if (row == null) return false;
            if (_vm.Files == null) return false;
            
            return _vm.Files.Contains(row);
        }

        public void Execute(object? parameter)
        {
            var row = parameter as FileRow;
            if (row == null) return;

            // Remove the row
            _vm.Files.Remove(row);

        }
    }
}
