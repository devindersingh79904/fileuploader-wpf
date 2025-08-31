using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace FileUploader.ViewModels.Commands
{
    public class SelectFilesCommand : ICommand
    {
        private const long MAX_SIZE_PER_FILE_IN_MB = 25;
        private const long MAX_SIZE = MAX_SIZE_PER_FILE_IN_MB * 1024 * 1024;   // 25 MB in bytes
        private const int MAX_FILES = 10;                 
        private const string FILTER_TEXT = "Text files (*.txt)|*.txt";



        private readonly MainViewModel _vm;

        public SelectFilesCommand(MainViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
           return _vm.Files.Count < 1;
        }


        public void Execute(object? parameter)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = FILTER_TEXT
            };

            var ok = dlg.ShowDialog();
            if (ok == true && dlg.FileNames.Length > 0)
            {
                // ✅ now dlg.FileNames has the selected files
                if (dlg.FileNames.Length > MAX_FILES)
                {
                    MessageBox.Show(
                        $"You can only select up to {MAX_FILES} files.",
                        "Limit Exceeded",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                // ✅ enforce file size limit
                var allowed = dlg.FileNames
                                 .Where(path => new FileInfo(path).Length <= MAX_SIZE)
                                 .ToArray();

                if (allowed.Length != dlg.FileNames.Length)
                {
                    MessageBox.Show(
                        $"Some files were skipped because they are larger than {MAX_SIZE_PER_FILE_IN_MB} MB.",
                        "File Size Limit",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                }

                if (allowed.Length > 0)
                {
                    _vm.AddFiles(allowed);
                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
}
