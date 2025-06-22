using CommonLib.Models;

namespace CommonLib.Events;

public class DownloadProgressEventArgs : EventArgs
{
    public DownloadProgress Progress { get; }
        
    public DownloadProgressEventArgs(DownloadProgress progress)
    {
        Progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }
}