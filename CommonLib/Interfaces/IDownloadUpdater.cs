namespace CommonLib.Interfaces;

public interface IDownloadUpdater
{
    Task<string?> DownloadAndExtractLatestUpdaterAsync(CancellationToken ct);
}