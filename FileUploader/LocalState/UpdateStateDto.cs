namespace FileUploader.LocalState
{
    public class UploadStateDto
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileId { get; set; } = string.Empty;
        public string UploadId { get; set; } = string.Empty;

        public int UploadedParts { get; set; }
        public int TotalParts { get; set; }
        public int ProgressPercent { get; set; }
    }
}
