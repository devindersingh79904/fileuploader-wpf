using System;
using System.Windows.Input;
using FileUploader.ViewModels;

namespace FileUploader.ViewModels.Commands
{
    public class NewSessionCommand : ICommand
    {
        private readonly MainViewModel _vm;
        private bool _isRunning;

        public NewSessionCommand(MainViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        }

        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        public bool CanExecute(object? parameter) => !_isRunning;

        public async void Execute(object? parameter)
        {
            if (_isRunning) return;
            try
            {
                _isRunning = true;
                RaiseCanExecuteChanged();

                // NOTE: MainViewModel exposes InitSessionAsync (not CreateNewSessionAsync)
                await _vm.InitSessionAsync();
            }
            finally
            {
                _isRunning = false;
                RaiseCanExecuteChanged();
            }
        }
    }
}
