using System;

namespace CommonLib.Models
{
    public class DownloadProgress
    {
        public long TotalBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public double PercentComplete { get; set; }
        public string Status { get; set; } = string.Empty;
        public TimeSpan ElapsedTime { get; set; }
        public double DownloadSpeedBytesPerSecond { get; set; }
        
        public string FormattedSpeed => FormatSpeed(DownloadSpeedBytesPerSecond);
        public string FormattedSize => $"{FormatBytes(DownloadedBytes)} / {FormatBytes(TotalBytes)}";
        
        public void CalculatePercentComplete()
        {
            PercentComplete = TotalBytes > 0 ? (double)DownloadedBytes / TotalBytes * 100 : 0;
        }
        
        private static string FormatSpeed(double bytesPerSecond)
        {
            return bytesPerSecond switch
            {
                >= 1024 * 1024 * 1024 => $"{bytesPerSecond / (1024 * 1024 * 1024):F2} GB/s",
                >= 1024 * 1024 => $"{bytesPerSecond / (1024 * 1024):F2} MB/s",
                >= 1024 => $"{bytesPerSecond / 1024:F2} KB/s",
                _ => $"{bytesPerSecond:F0} B/s"
            };
        }
        
        private static string FormatBytes(long bytes)
        {
            return bytes switch
            {
                >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
                >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F2} MB",
                >= 1024 => $"{bytes / 1024.0:F2} KB",
                _ => $"{bytes} B"
            };
        }
    }
}