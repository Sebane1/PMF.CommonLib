
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommonLib.Enums;
using CommonLib.Extensions;
using CommonLib.Interfaces;
using CommonLib.Models;
using Newtonsoft.Json.Linq;
using NLog;
using SevenZipExtractor;

namespace CommonLib.Services;

public class Aria2Service : IAria2Service
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        
    private readonly object _syncLock = new();
        
    private Task<bool>? _ensureAria2Task;

    private bool _aria2Ready;
    private const string Aria2LatestReleaseApi = "https://api.github.com/repos/aria2/aria2/releases/latest";

    public string Aria2Folder { get; }
    public string Aria2ExePath => Path.Combine(Aria2Folder, "aria2c.exe");

    public Aria2Service(string baseInstallFolder)
    {
        var libFolder = Path.Combine(baseInstallFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), "Lib");

        if (!Directory.Exists(libFolder))
        {
            Directory.CreateDirectory(libFolder);
            _logger.Info("Created Lib directory at {LibFolder}", libFolder);
        }

        Aria2Folder = libFolder;
    }

    public async Task<bool> EnsureAria2AvailableAsync(CancellationToken ct)
    {
        if (_aria2Ready)
            return true;
            
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.Warn("Service is only supported on Windows platforms in this implementation.");
            return false;
        }
            
        lock (_syncLock)
        {
            if (_ensureAria2Task == null)
            {
                _ensureAria2Task = InternalEnsureAria2AvailableAsync(ct);
            }
        }
            
        var installed = await _ensureAria2Task.ConfigureAwait(false);
            
        if (!installed)
        {
            lock (_syncLock)
            {
                _ensureAria2Task = null;
            }
        }

        return installed;
    }

    public async Task<bool> DownloadFileAsync(
        string fileUrl, 
        string downloadDirectory, 
        CancellationToken ct,
        IProgress<DownloadProgress>? progress = null)
    {
        var isReady = await EnsureAria2AvailableAsync(ct).ConfigureAwait(false);
        if (!isReady)
            return false;

        try
        {
            var sanitizedDirectory = downloadDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            );

            var rawFileName = Path.GetFileName(new Uri(fileUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(rawFileName))
                rawFileName = "download.bin";

            var finalFileName = Uri.UnescapeDataString(rawFileName);

            // Check if the drive where the file will be saved is an SSD or HDD
            var driveType = DriveTypeDetector.GetDriveType(sanitizedDirectory);
                
            // If it's an SSD, use --file-allocation=none, otherwise leave it out
            var extraAria2Args = driveType == DriveTypeCommon.Ssd
                ? "--log-level=debug --file-allocation=none"
                : "--log-level=debug";

            var arguments = $"\"{fileUrl}\" --dir=\"{sanitizedDirectory}\" --out=\"{finalFileName}\" {extraAria2Args}";

            var startInfo = new ProcessStartInfo
            {
                FileName = Aria2ExePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _logger.Debug("Launching aria2 with arguments: {Args}", startInfo.Arguments);

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.Error("Failed to start aria2 at {Aria2ExePath}", Aria2ExePath);
                return false;
            }

            var stopwatch = Stopwatch.StartNew();
                
            var progressTask = progress != null 
                ? Aria2ProgressMonitor.MonitorProgressAsync(process, progress, stopwatch, ct)
                : Task.CompletedTask;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(30));

            await Task.WhenAll(
                process.WaitForExitAsync(timeoutCts.Token),
                progressTask
            ).ConfigureAwait(false);

            stopwatch.Stop();

            var stdOut = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            var stdErr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(stdOut))
            {
                _logger.Debug("[aria2 STDOUT] {Output}", stdOut.Trim());
            }
            if (!string.IsNullOrWhiteSpace(stdErr))
            {
                _logger.Debug("[aria2 STDERR] {Output}", stdErr.Trim());
            }

            if (process.ExitCode == 0)
            {
                progress?.Report(new DownloadProgress
                {
                    Status = "Completed",
                    PercentComplete = 100,
                    ElapsedTime = stopwatch.Elapsed
                });

                _logger.Info(
                    "aria2 finished downloading {FileUrl} to {Directory}\\{FileName}",
                    fileUrl,
                    sanitizedDirectory,
                    finalFileName
                );
                return true;
            }

            _logger.Error("aria2 exited with code {Code} for {Url}", process.ExitCode, fileUrl);
            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Download canceled for {FileUrl}", fileUrl);
            progress?.Report(new DownloadProgress { Status = "Cancelled" });
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error downloading {FileUrl} via aria2", fileUrl);
            progress?.Report(new DownloadProgress { Status = "Error" });
            return false;
        }
    }
    
    private async Task<bool> InternalEnsureAria2AvailableAsync(CancellationToken ct)
    {
        // If the binary doesn't exist, attempt to download/install
        if (!File.Exists(Aria2ExePath))
        {
            _logger.Info("aria2 not found at '{Path}'. Checking the latest release on GitHub...", Aria2ExePath);
            var installed = await DownloadAndInstallAria2FromLatestAsync(ct).ConfigureAwait(false);
            _aria2Ready = installed;
            return installed;
        }

        _logger.Info("aria2 located at {Path}", Aria2ExePath);
        _aria2Ready = true;
        return true;
    }

    private async Task<bool> DownloadAndInstallAria2FromLatestAsync(CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(Aria2Folder))
            {
                Directory.CreateDirectory(Aria2Folder);
            }
            
            var downloadUrl = await FetchWin64AssetUrlWithRetriesAsync(ct).ConfigureAwait(false);
            
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                _logger.Warn("API failed or returned no result. Falling back to HTML scraping.");
                downloadUrl = await FetchWin64AssetUrlFromHtmlAsync(ct).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                _logger.Error("Failed to find a suitable Windows 64-bit asset URL.");
                return false;
            }

            var zipPath = Path.Combine(Aria2Folder, "aria2_latest_win64.zip");

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Atomos/Aria2Service");
                var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                _logger.Info("Downloading aria2 from {Url}", downloadUrl);

                await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            _logger.Info("Extracting aria2 files to {ExtractPath}", Aria2Folder);

            using (var archiveFile = new ArchiveFile(zipPath))
            {
                archiveFile.Extract(Aria2Folder, overwrite: true);
            }

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            if (!File.Exists(Aria2ExePath))
            {
                var exeCandidate = Directory
                    .GetFiles(Aria2Folder, "aria2c.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (exeCandidate != null)
                {
                    var targetPath = Path.Combine(Aria2Folder, Path.GetFileName(exeCandidate));
                    if (!File.Exists(targetPath))
                    {
                        File.Move(exeCandidate, targetPath);

                        _logger.Info("Found aria2c.exe in a subdirectory; moved it to {TargetPath}", targetPath);

                        var candidateFolder = Path.GetDirectoryName(exeCandidate);
                        if (!string.IsNullOrEmpty(candidateFolder) &&
                            !candidateFolder.Equals(Aria2Folder, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                _logger.Info("Removing extracted folder {Folder}", candidateFolder);
                                Directory.Delete(candidateFolder, recursive: true);
                                _logger.Info("Successfully removed folder {Folder}", candidateFolder);
                            }
                            catch (Exception ex)
                            {
                                _logger.Warn(ex, "Failed to remove folder {Folder}", candidateFolder);
                            }
                        }
                    }
                }
            }

            if (!File.Exists(Aria2ExePath))
            {
                _logger.Error("Setup failed. {Aria2ExePath} not found after extraction.", Aria2ExePath);
                return false;
            }

            _logger.Info("Successfully installed aria2 at {Aria2ExePath}", Aria2ExePath);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Download or setup was canceled.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error fetching/installing aria2.");
            return false;
        }
    }
        
    private async Task<string?> FetchWin64AssetUrlWithRetriesAsync(CancellationToken ct)
    {
        return await RetryWithBackoffAsync(
            async () =>
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Atomos/Aria2Service");

                var json = await client.GetStringAsync(Aria2LatestReleaseApi, ct).ConfigureAwait(false);
                var doc = JToken.Parse(json);

                if (doc["assets"] is JArray assetsArray)
                {
                    foreach (var asset in assetsArray)
                    {
                        var assetName = (string?)asset["name"] ?? string.Empty;
                        var downloadUrl = (string?)asset["browser_download_url"] ?? string.Empty;

                        if (assetName.Contains("win-64", StringComparison.OrdinalIgnoreCase) &&
                            assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            return downloadUrl;
                        }
                    }
                }

                return null;
            },
            maxRetries: 3,
            initialDelay: TimeSpan.FromSeconds(2),
            ct
        );
    }
        
    private async Task<string?> FetchWin64AssetUrlFromHtmlAsync(CancellationToken ct)
    {
        const string aria2ReleasesUrl = "https://github.com/aria2/aria2/releases/latest";

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Atomos/Aria2Service");

            var html = await client.GetStringAsync(aria2ReleasesUrl, ct).ConfigureAwait(false);

            var matches = Regex.Matches(html, @"href=[""'](\/aria2\/aria2\/releases\/download\/[^""']*?win-64[^""']*?\.zip)[""']", RegexOptions.IgnoreCase);

            if (matches.Count > 0)
            {
                var relativeUrl = matches[0].Groups[1].Value;
                return "https://github.com" + relativeUrl;
            }

            _logger.Warn("No suitable Windows 64-bit asset URL found on the releases page.");
        }
        catch (OperationCanceledException)
        {
            _logger.Warn("Fetching Windows 64-bit asset URL from HTML was canceled.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch the latest aria2 release data from GitHub HTML.");
        }

        return null;
    }
        
    private async Task<T?> RetryWithBackoffAsync<T>(
        Func<Task<T?>> action,
        int maxRetries,
        TimeSpan initialDelay,
        CancellationToken ct)
    {
        var delay = initialDelay;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var result = await action();
                if (result != null)
                    return result;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.Warn(ex, "Attempt {Attempt}/{MaxRetries} failed. Retrying in {Delay}...", attempt, maxRetries, delay);
            }

            if (attempt < maxRetries)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
            }
        }

        _logger.Error("All retries failed after {MaxRetries} attempts.", maxRetries);
        return default;
    }
}