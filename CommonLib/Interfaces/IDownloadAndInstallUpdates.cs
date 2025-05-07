namespace CommonLib.Interfaces;

public interface IDownloadAndInstallUpdates
{
    Task<(bool success, string downloadPath)> DownloadAndInstallAsync(string currentVersion);
}