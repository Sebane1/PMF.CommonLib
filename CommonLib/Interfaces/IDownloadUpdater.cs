using CommonLib.Models;

namespace CommonLib.Interfaces;

public interface IDownloadUpdater
{
    Task<string?> DownloadAndExtractLatestUpdaterAsync(CancellationToken ct,
        IProgress<DownloadProgress>? progress = null);
}