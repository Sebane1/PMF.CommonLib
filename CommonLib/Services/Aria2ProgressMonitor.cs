using System.Diagnostics;
using System.Text;
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
        try
        {
            var buffer = new char[4096];
            var output = new StringBuilder();
                
            while (!process.HasExited && !ct.IsCancellationRequested)
            {
                if (process.StandardOutput.Peek() >= 0)
                {
                    var bytesRead = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        output.Append(buffer, 0, bytesRead);
                        var lines = output.ToString().Split('\n');
                        
                        output.Clear();
                        if (lines.Length > 0 && !lines[^1].EndsWith('\n'))
                        {
                            output.Append(lines[^1]);
                        }
                        
                        for (int i = 0; i < lines.Length - 1; i++)
                        {
                            if (Aria2ProgressParser.TryParseProgressLine(lines[i], stopwatch.Elapsed, out var progressData) 
                                && progressData != null)
                            {
                                progress.Report(progressData);
                            }
                        }
                    }
                }
                    
                await Task.Delay(100, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Error monitoring aria2 progress");
        }
    }
}