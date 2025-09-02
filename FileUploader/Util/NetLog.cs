using System;
using System.Diagnostics;

namespace FileUploadClient.Wpf.Util
{
    public static class NetLog
    {
        public static void Line(string message)
        {
            var s = $"[{DateTime.UtcNow:O}] {message}";
            Debug.WriteLine(s);
            Console.WriteLine(s);
        }

        public static string Trunc(string? s, int max = 1000)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s[..max] + " ...[truncated]";
        }
    }
}
