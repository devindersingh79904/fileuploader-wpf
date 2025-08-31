namespace FileUploadClient.Wpf.Config
{
    public static class AppConfig
    {
        // ✅ Point to your Render deployment
        public const string BaseUrl = "https://fileuploaddemobackend.onrender.com/api/v1/upload/";

        // ✅ Chunk size for multipart uploads (S3 requires >= 5 MB except last part)
        public const int ChunkBytes = 5 * 1024 * 1024;
    }
}
