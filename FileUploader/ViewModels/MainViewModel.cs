using FileUploader.Dtos;
using FileUploader.Dtos.Responses;
using FileUploader.Models;
using FileUploader.ViewModels.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FileUploader.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<FileRow> Files { get; }

    public SelectFilesCommand SelectFilesCommand { get; }
        public DeleteFileCommand DeleteFileCommand { get; set; }
        public StartUploadCommand StartUploadCommand { get; set; }
        public PauseAllCommand PauseAllCommand { get; }
        public ResumeAllCommand ResumeAllCommand { get; }
        public CancelAllCommand CancelAllCommand { get; }



        private bool _sessionCreated = false;

        private string _sessionId;
        public string SessionId
        {
            get { return _sessionId; }
            set
            {
                if (_sessionId != value)
                {
                    _sessionId = value;
                    OnPropertyChanged("SessionId");
                }
            }
        }

        private string _sessionStatus;
        public string SessionStatus
        {
            get { return _sessionStatus; }
            set
            {
                if (_sessionStatus != value)
                {
                    _sessionStatus = value;
                    OnPropertyChanged("SessionStatus");
                }
            }
        }

        private FileUploadApi _api;



        private double _overallProgress = 0; // 0..100
        public double OverallProgress
        {
            get { return _overallProgress; }
            set
            {
                if (_overallProgress != value)
                {
                    _overallProgress = value;
                    OnPropertyChanged(nameof(OverallProgress));
                }
            }
        }

        private string _overallProgressText = "Overall: 0% (0 of 0)";
        public string OverallProgressText
        {
            get { return _overallProgressText; }
            set
            {
                if (_overallProgressText != value)
                {
                    _overallProgressText = value;
                    OnPropertyChanged(nameof(OverallProgressText));
                }
            }
        }


        public async Task InitSessionAsync()
        {
            if (_sessionCreated) return;   // prevents double calls
            _sessionCreated = true;
            try
            {
                SessionStatus = "Creating session...";
                StartSessionRequest request = new StartSessionRequest();
                request.UserId = "c001";

                var token = new System.Threading.CancellationToken();
                StartSessionResponse response = await _api.StartSessionAsync(request, token);

                SessionId = response.SessionId;
                SessionStatus = "Session created: " + SessionId;
            }
            catch (System.Net.Http.HttpRequestException httpEx)
            {
                SessionStatus = "Network error: " + httpEx.Message;
            }
            catch (System.Exception ex)
            {
                SessionStatus = "Failed: " + ex.Message;
            }
        }





        public MainViewModel()
        {
            // pass 'this' so the command can call back into the VM (no delegates)
            Files = new ObservableCollection<FileRow>();

            SelectFilesCommand = new SelectFilesCommand(this);
            DeleteFileCommand = new DeleteFileCommand(this);
            StartUploadCommand = new StartUploadCommand(this);
            PauseAllCommand = new PauseAllCommand(this);
            ResumeAllCommand = new ResumeAllCommand(this);
            CancelAllCommand = new CancelAllCommand(this);



            _api = new FileUploadApi();


            Files.CollectionChanged += OnFilesCollectionChanged;
        }

        private void OnFilesCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Enable Start when Files.Count > 0, disable when it goes back to 0.
            StartUploadCommand.RaiseCanExecuteChanged();
            PauseAllCommand.RaiseCanExecuteChanged();
            ResumeAllCommand.RaiseCanExecuteChanged();
            CancelAllCommand.RaiseCanExecuteChanged();
            OverallProgressText = $"Overall: {OverallProgress:0}% ({Files.Count} file(s))";
            // If you want, also notify Delete etc. later.
            // DeleteFileCommand.RaiseCanExecuteChanged();
        }

        public void AddFiles(string[] filePaths)
        {
            int start = Files.Count + 1;
            for (int i = 0; i < filePaths.Length; i++)
            {
                Files.Add(FileRow.FromPath(filePaths[i], start + i));
            }


            StartUploadCommand.RaiseCanExecuteChanged();
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }

}
