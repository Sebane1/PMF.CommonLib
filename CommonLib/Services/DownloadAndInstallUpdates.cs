using System.Runtime.InteropServices;
using CommonLib.Interfaces;
using NLog;
using SevenZipExtractor;

namespace CommonLib.Services;

public class DownloadAndInstallUpdates : IDownloadAndInstallUpdates
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IAria2Service _aria2Service;
    private readonly IUpdateService _updateService;
    private readonly IAppArguments _appArgs;

    public DownloadAndInstallUpdates(
        IAria2Service aria2Service, 
        IUpdateService updateService,
        IAppArguments appArgs)
    {
        _aria2Service = aria2Service;
        _updateService = updateService;
        _appArgs = appArgs;
    }

    public async Task<(bool success, string downloadPath)> DownloadAndInstallAsync(string currentVersion)
    {
        try
        {
            // Include the repository in your logging
            _logger.Info("Checking if update is needed for version {Version} in repo {Repository}",
                currentVersion, _appArgs.GitHubRepo);

            // Call NeedsUpdateAsync with the repository
            var needsUpdate = await _updateService.NeedsUpdateAsync(currentVersion, _appArgs.GitHubRepo);
            if (!needsUpdate)
            {
                _logger.Info("No update needed. Current version is up-to-date.");
                return (false, string.Empty);
            }

            await _aria2Service.EnsureAria2AvailableAsync(CancellationToken.None).ConfigureAwait(false);

            var tempDir = Path.Combine(Path.GetTempPath(), "Council Of Tsukuyomi", "Updates");
            Directory.CreateDirectory(tempDir);

            // Get the zip links passing the repository param
            var zipUrls = await _updateService.GetUpdateZipLinksAsync(currentVersion, _appArgs.GitHubRepo);
            if (zipUrls.Count == 0)
            {
                _logger.Warn("No .zip assets found for the latest release in {Repository}. Update cannot proceed.",
                    _appArgs.GitHubRepo);
                return (false, string.Empty);
            }

            var osFilteredUrls = zipUrls.Where(IsOsCompatibleAsset).ToList();
            if (osFilteredUrls.Count == 0)
            {
                _logger.Warn("No assets matching the current OS were found in {Repository}.", _appArgs.GitHubRepo);
                return (false, string.Empty);
            }

            var downloadedPaths = new List<string>();
            foreach (var url in osFilteredUrls)
            {
                _logger.Info("Starting download for {Url} from {Repository}", url, _appArgs.GitHubRepo);
                var success = await _aria2Service.DownloadFileAsync(url, tempDir, CancellationToken.None);
                if (!success)
                {
                    _logger.Error("Failed to download {Url} from {Repository}. Aborting update.", url, _appArgs.GitHubRepo);
                    return (false, string.Empty);
                }

                var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
                var finalPath = Path.Combine(tempDir, Uri.UnescapeDataString(fileName));
                downloadedPaths.Add(finalPath);
            }

            foreach (var zipPath in downloadedPaths)
            {
                var extractOk = await ExtractZipsAsync(zipPath, tempDir, CancellationToken.None);
                if (!extractOk)
                {
                    _logger.Error("Failed to extract {ZipPath} from {Repository}. Aborting update.", zipPath, _appArgs.GitHubRepo);
                    return (false, string.Empty);
                }
            }

            _logger.Info("All OS-compatible .zip assets have been downloaded and extracted to {Directory} for {Repository}",
                tempDir, _appArgs.GitHubRepo);
            return (true, tempDir);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while downloading and installing updates for {Repository}", _appArgs.GitHubRepo);
            return (false, string.Empty);
        }
    }

    private async Task<bool> ExtractZipsAsync(string zipPath, string extractFolder, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(zipPath))
            {
                _logger.Warn("Zip file {ZipPath} not found.", zipPath);
                return false;
            }

            await Task.Yield();

            using (var archiveFile = new ArchiveFile(zipPath))
            {
                archiveFile.Extract(extractFolder, overwrite: true);
            }

            File.Delete(zipPath);
            _logger.Info("Extracted and deleted {ZipPath}", zipPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to extract zip file at {ZipPath}", zipPath);
            return false;
        }
    }

    private bool IsOsCompatibleAsset(string assetUrl)
    {
        var fileName = assetUrl.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return fileName.Contains("Windows", StringComparison.OrdinalIgnoreCase);

        return fileName.Contains("Linux", StringComparison.OrdinalIgnoreCase);
    }
}