using System.Diagnostics;
using CommonLib.Models;
using NLog;

namespace CommonLib.Services;

public class Aria2ProgressMonitor
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        
    public static async Task MonitorProgressAsync(
        Process process, 
        IProgress<DownloadProgress> progress, 
        Stopwatch stopwatch,
        CancellationToken ct)
    {
        _logger.Info("=== STARTING ARIA2 PROGRESS MONITORING ===");
        
        try
        {
            var lastProgressReport = DateTime.Now;
            var isCompleted = false;
            var lastProgressData = new DownloadProgress();
            
            // Send initial progress
            progress.Report(new DownloadProgress 
            { 
                Status = "Downloading...", 
                PercentComplete = 0,
                ElapsedTime = stopwatch.Elapsed 
            });

            // Create separate tasks for reading stdout and stderr
            var stdoutTask = ReadStreamAsync(process.StandardOutput, "STDOUT", stopwatch, progress, ct, lastProgressData);
            var stderrTask = ReadStreamAsync(process.StandardError, "STDERR", stopwatch, progress, ct, lastProgressData);
            
            // Create a task that monitors for basic progress updates
            var progressMonitorTask = MonitorBasicProgressAsync(progress, stopwatch, ct, () => isCompleted, lastProgressData);
            
            // Wait for any of the tasks to complete or the process to exit
            var allTasks = new List<Task> { stdoutTask, stderrTask, progressMonitorTask };
            
            while (!process.HasExited && !ct.IsCancellationRequested)
            {
                // Wait for a short time and check process status
                await Task.Delay(250, ct);
                
                // Log process status periodically
                if (DateTime.Now - lastProgressReport > TimeSpan.FromSeconds(5))
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            _logger.Debug("Process still running, PID: {PID}, Elapsed: {Elapsed}", 
                                process.Id, stopwatch.Elapsed);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Error checking process status");
                    }
                    lastProgressReport = DateTime.Now;
                }
            }
            
            _logger.Info("Aria2 process has exited or monitoring was cancelled");
            isCompleted = true; // Stop the basic progress monitor
            
            // Cancel the monitoring tasks
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            
            // Wait a bit for the tasks to complete
            try
            {
                await Task.WhenAll(allTasks).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (TimeoutException)
            {
                _logger.Debug("Stream reading tasks didn't complete within timeout");
            }
            
            _logger.Info("Aria2 progress monitoring finished.");
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Aria2 progress monitoring was cancelled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error monitoring aria2 progress");
        }
        
        _logger.Info("=== ARIA2 PROGRESS MONITORING ENDED ===");
    }
    
    private static async Task ReadStreamAsync(
        StreamReader reader, 
        string streamName, 
        Stopwatch stopwatch, 
        IProgress<DownloadProgress> progress, 
        CancellationToken ct,
        DownloadProgress lastProgressData)
    {
        try
        {
            var progressCount = 0;
            var lineCount = 0;
            
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break; // End of stream
                    
                    lineCount++;
                    
                    // Log lines that look like they contain progress information
                    if (line.Contains("[#") || line.Contains("DL:") || line.Contains("%") || line.Contains("SIZE:"))
                    {
                        _logger.Info("{StreamName} [{Count}] PROGRESS: {Line}", streamName, lineCount, line);
                    }
                    else
                    {
                        _logger.Debug("{StreamName} [{Count}]: {Line}", streamName, lineCount, line);
                    }
                    
                    if (TryParseAndReportProgress(line, stopwatch.Elapsed, progress, ref progressCount, lastProgressData))
                    {
                        _logger.Info("Progress parsed from {StreamName}, update #{Count}", streamName, progressCount);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Error reading line from {StreamName}", streamName);
                    await Task.Delay(100, ct); // Brief delay before retrying
                }
            }
            
            _logger.Debug("Finished reading {StreamName}, total lines: {LineCount}, progress updates: {ProgressCount}", 
                streamName, lineCount, progressCount);
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Reading {StreamName} was cancelled", streamName);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Error in {StreamName} reading task", streamName);
        }
    }
    
    private static async Task MonitorBasicProgressAsync(
        IProgress<DownloadProgress> progress, 
        Stopwatch stopwatch, 
        CancellationToken ct,
        Func<bool> isCompleted,
        DownloadProgress lastProgressData)
    {
        try
        {
            var lastUpdate = DateTime.Now;
            
            while (!ct.IsCancellationRequested && !isCompleted())
            {
                await Task.Delay(3000, ct); // Update every 3 seconds
                
                if (isCompleted()) break; // Don't send updates after completion
                
                var timeSinceLastUpdate = DateTime.Now - lastUpdate;
                if (timeSinceLastUpdate > TimeSpan.FromSeconds(3))
                {
                    // Create a basic progress update with cached information
                    var basicProgress = new DownloadProgress 
                    { 
                        Status = $"Downloading... ({stopwatch.Elapsed:mm\\:ss} elapsed)", 
                        ElapsedTime = stopwatch.Elapsed,
                        PercentComplete = lastProgressData.PercentComplete,
                        DownloadSpeedBytesPerSecond = lastProgressData.DownloadSpeedBytesPerSecond,
                        TotalBytes = lastProgressData.TotalBytes,
                        DownloadedBytes = lastProgressData.DownloadedBytes
                    };
                    
                    _logger.Debug("Sending basic progress update, elapsed: {Elapsed}, percent: {Percent}%", 
                        stopwatch.Elapsed, basicProgress.PercentComplete);
                    progress.Report(basicProgress);
                    lastUpdate = DateTime.Now;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Basic progress monitoring was cancelled");
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Error in basic progress monitoring");
        }
    }
    
    private static bool TryParseAndReportProgress(
        string line, 
        TimeSpan elapsed, 
        IProgress<DownloadProgress> progress, 
        ref int progressCount,
        DownloadProgress lastProgressData)
    {
        if (Aria2ProgressParser.TryParseProgressLine(line, elapsed, out var progressData) && progressData != null)
        {
            progressCount++;
            _logger.Info("Progress update #{Count}: {Percent}% - {Status} - Speed: {Speed} B/s - Size: {Downloaded}/{Total}", 
                progressCount, progressData.PercentComplete, progressData.Status, 
                progressData.DownloadSpeedBytesPerSecond, progressData.DownloadedBytes, progressData.TotalBytes);
            
            // Update the cached progress data
            if (progressData.PercentComplete > 0) lastProgressData.PercentComplete = progressData.PercentComplete;
            if (progressData.DownloadSpeedBytesPerSecond > 0) lastProgressData.DownloadSpeedBytesPerSecond = progressData.DownloadSpeedBytesPerSecond;
            if (progressData.TotalBytes > 0) lastProgressData.TotalBytes = progressData.TotalBytes;
            if (progressData.DownloadedBytes > 0) lastProgressData.DownloadedBytes = progressData.DownloadedBytes;
            if (!string.IsNullOrEmpty(progressData.Status)) lastProgressData.Status = progressData.Status;
            lastProgressData.ElapsedTime = progressData.ElapsedTime;
            
            progress.Report(progressData);
            return true;
        }
        return false;
    }
}