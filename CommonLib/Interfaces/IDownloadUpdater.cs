namespace PenumbraModForwarder.Common.Interfaces;

public interface IDownloadUpdater
{
    Task<string?> DownloadAndExtractLatestUpdaterAsync(CancellationToken ct);
}