
using System.Runtime.InteropServices;
using CommonLib.Interfaces;
using CommonLib.Models;
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

    public async Task<(bool success, string downloadPath)> DownloadAndInstallAsync(
        string currentVersion, 
        IProgress<DownloadProgress>? progress = null)
    {
        try
        {
            _logger.Info("=== DOWNLOAD AND INSTALL STARTED ===");
            _logger.Info("Progress reporter is {ProgressStatus}", progress != null ? "PROVIDED" : "NULL");
            
            // Include the repository in your logging
            _logger.Info("Checking if update is needed for version {Version} in repo {Repository}",
                currentVersion, _appArgs.GitHubRepo);

            if (progress != null)
            {
                _logger.Info("Reporting initial progress: Checking for updates...");
                progress.Report(new DownloadProgress { Status = "Checking for updates..." });
            }

            // Call NeedsUpdateAsync with the repository
            var needsUpdate = await _updateService.NeedsUpdateAsync(currentVersion, _appArgs.GitHubRepo);
            if (!needsUpdate)
            {
                _logger.Info("No update needed. Current version is up-to-date.");
                progress?.Report(new DownloadProgress { Status = "No update needed" });
                return (false, string.Empty);
            }

            _logger.Info("Update needed, proceeding with download process");
            
            if (progress != null)
            {
                _logger.Info("Reporting progress: Preparing aria2...");
                progress.Report(new DownloadProgress { Status = "Preparing aria2..." });
            }
            
            await _aria2Service.EnsureAria2AvailableAsync(CancellationToken.None).ConfigureAwait(false);

            var tempDir = Path.Combine(Path.GetTempPath(), "Council Of Tsukuyomi", "Updates");
            Directory.CreateDirectory(tempDir);
            _logger.Info("Created temp directory: {TempDir}", tempDir);

            if (progress != null)
            {
                _logger.Info("Reporting progress: Getting download links...");
                progress.Report(new DownloadProgress { Status = "Getting download links..." });
            }

            // Get the zip links passing the repository param
            var zipUrls = await _updateService.GetUpdateZipLinksAsync(currentVersion, _appArgs.GitHubRepo);
            _logger.Info("Found {UrlCount} zip URLs", zipUrls.Count);
            
            if (zipUrls.Count == 0)
            {
                _logger.Warn("No .zip assets found for the latest release in {Repository}. Update cannot proceed.",
                    _appArgs.GitHubRepo);
                progress?.Report(new DownloadProgress { Status = "No update files found" });
                return (false, string.Empty);
            }

            var osFilteredUrls = zipUrls.Where(IsOsCompatibleAsset).ToList();
            _logger.Info("After OS filtering, {FilteredCount} URLs remain", osFilteredUrls.Count);
            
            if (osFilteredUrls.Count == 0)
            {
                _logger.Warn("No assets matching the current OS were found in {Repository}.", _appArgs.GitHubRepo);
                progress?.Report(new DownloadProgress { Status = "No compatible files found" });
                return (false, string.Empty);
            }

            // Download files with progress tracking
            var downloadedPaths = new List<string>();
            for (int i = 0; i < osFilteredUrls.Count; i++)
            {
                var url = osFilteredUrls[i];
                _logger.Info("=== STARTING DOWNLOAD {Current}/{Total} ===", i + 1, osFilteredUrls.Count);
                _logger.Info("URL: {Url}", url);

                // Create a progress wrapper that adjusts the overall progress
                IProgress<DownloadProgress>? downloadProgress = null;
                if (progress != null)
                {
                    _logger.Info("Creating progress wrapper for download {Current}/{Total}", i + 1, osFilteredUrls.Count);
                    downloadProgress = new Progress<DownloadProgress>(p => ReportDownloadProgress(p, i, osFilteredUrls.Count, progress));
                }
                else
                {
                    _logger.Warn("No progress reporter available for download");
                }

                var success = await _aria2Service.DownloadFileAsync(url, tempDir, CancellationToken.None, downloadProgress);
                _logger.Info("Download {Current}/{Total} completed with success: {Success}", i + 1, osFilteredUrls.Count, success);
                
                if (!success)
                {
                    _logger.Error("Failed to download {Url} from {Repository}. Aborting update.", url, _appArgs.GitHubRepo);
                    progress?.Report(new DownloadProgress { Status = "Download failed" });
                    return (false, string.Empty);
                }

                var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
                var finalPath = Path.Combine(tempDir, Uri.UnescapeDataString(fileName));
                downloadedPaths.Add(finalPath);
                _logger.Info("Added downloaded file path: {FilePath}", finalPath);
            }

            // Extract files with progress
            _logger.Info("=== STARTING EXTRACTION PHASE ===");
            if (progress != null)
            {
                _logger.Info("Reporting extraction progress: 90%");
                progress.Report(new DownloadProgress { Status = "Extracting files...", PercentComplete = 90 });
            }
            
            for (int i = 0; i < downloadedPaths.Count; i++)
            {
                var zipPath = downloadedPaths[i];
                var extractProgress = 90 + (i * 10 / downloadedPaths.Count); // 90-100% for extraction
                _logger.Info("Extracting file {Current}/{Total}, progress: {Progress}%", i + 1, downloadedPaths.Count, extractProgress);
                
                if (progress != null)
                {
                    progress.Report(new DownloadProgress 
                    { 
                        Status = $"Extracting file {i + 1}/{downloadedPaths.Count}...", 
                        PercentComplete = extractProgress 
                    });
                }

                var extractOk = await ExtractZipsAsync(zipPath, tempDir, CancellationToken.None);
                if (!extractOk)
                {
                    _logger.Error("Failed to extract {ZipPath} from {Repository}. Aborting update.", zipPath, _appArgs.GitHubRepo);
                    progress?.Report(new DownloadProgress { Status = "Extraction failed" });
                    return (false, string.Empty);
                }
            }

            _logger.Info("All OS-compatible .zip assets have been downloaded and extracted to {Directory} for {Repository}",
                tempDir, _appArgs.GitHubRepo);
            
            if (progress != null)
            {
                _logger.Info("Reporting final progress: 100%");
                progress.Report(new DownloadProgress { Status = "Download complete!", PercentComplete = 100 });
            }
            
            _logger.Info("=== DOWNLOAD AND INSTALL COMPLETED SUCCESSFULLY ===");
            return (true, tempDir);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while downloading and installing updates for {Repository}", _appArgs.GitHubRepo);
            progress?.Report(new DownloadProgress { Status = "Error occurred" });
            return (false, string.Empty);
        }
    }
    
    private void ReportDownloadProgress(DownloadProgress individualProgress, int currentFile, int totalFiles, IProgress<DownloadProgress> overallProgress)
    {
        _logger.Debug("=== PROGRESS REPORT ===");
        _logger.Debug("File {Current}/{Total}", currentFile + 1, totalFiles);
        _logger.Debug("Individual Progress: {Percent}%", individualProgress.PercentComplete);
        _logger.Debug("Downloaded: {Downloaded}/{Total} bytes", individualProgress.DownloadedBytes, individualProgress.TotalBytes);
        _logger.Debug("Speed: {Speed} bytes/sec", individualProgress.DownloadSpeedBytesPerSecond);
        _logger.Debug("Formatted Speed: {FormattedSpeed}", individualProgress.FormattedSpeed);
        _logger.Debug("Formatted Size: {FormattedSize}", individualProgress.FormattedSize);
        _logger.Debug("Status: {Status}", individualProgress.Status);

        // Calculate overall progress: each file gets equal weight
        var baseProgress = (currentFile * 100.0) / totalFiles;
        var fileProgress = individualProgress.PercentComplete / totalFiles;
        var totalProgress = baseProgress + fileProgress;
        
        // If the download is completed, show 89% to leave room for extraction
        // If it's actually completed (100%), allow it to show the full progress
        if (individualProgress.Status == "Completed" && individualProgress.PercentComplete >= 100)
        {
            totalProgress = Math.Min(89, totalProgress); // Cap at 89% for extraction phase
        }
        else
        {
            totalProgress = Math.Min(88, totalProgress); // Cap at 88% during download
        }

        var status = totalFiles > 1 
            ? $"Downloading file {currentFile + 1}/{totalFiles}... {individualProgress.FormattedSize} at {individualProgress.FormattedSpeed}"
            : individualProgress.Status ?? "Downloading...";

        _logger.Debug("Calculated total progress: {TotalProgress}%", totalProgress);
        _logger.Debug("Status to report: {Status}", status);

        var progressToReport = new DownloadProgress
        {
            Status = status,
            PercentComplete = totalProgress,
            DownloadSpeedBytesPerSecond = individualProgress.DownloadSpeedBytesPerSecond,
            ElapsedTime = individualProgress.ElapsedTime,
            TotalBytes = individualProgress.TotalBytes,
            DownloadedBytes = individualProgress.DownloadedBytes
        };

        _logger.Debug("Reporting overall progress to UI...");
        overallProgress.Report(progressToReport);
        _logger.Debug("=== END PROGRESS REPORT ===");
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
        _logger.Debug("Checking OS compatibility for file: {FileName}", fileName);

        bool isCompatible;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            isCompatible = fileName.Contains("Windows", StringComparison.OrdinalIgnoreCase);
            _logger.Debug("Windows OS - File compatible: {IsCompatible}", isCompatible);
        }
        else
        {
            isCompatible = fileName.Contains("Linux", StringComparison.OrdinalIgnoreCase);
            _logger.Debug("Linux OS - File compatible: {IsCompatible}", isCompatible);
        }

        return isCompatible;
    }
}