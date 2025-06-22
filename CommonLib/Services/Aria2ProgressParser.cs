using System;
using System.Text.RegularExpressions;
using CommonLib.Models;
using NLog;

namespace CommonLib.Services
{
    public static class Aria2ProgressParser
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        
        public static bool TryParseProgressLine(string line, TimeSpan elapsed, out DownloadProgress? progress)
        {
            progress = null;
            
            try
            {
                // Aria2 debug output typically contains progress information
                // Example patterns to look for:
                // [#1 SIZE:123456/789012(15%) CN:1 DL:45678B ETA:12s]
                
                var progressMatch = Regex.Match(line, @"SIZE:(\d+)/(\d+)\((\d+)%\).*?DL:(\d+[KMGT]?B)");
                if (progressMatch.Success)
                {
                    var downloaded = long.Parse(progressMatch.Groups[1].Value);
                    var total = long.Parse(progressMatch.Groups[2].Value);
                    var percent = double.Parse(progressMatch.Groups[3].Value);
                    var speedStr = progressMatch.Groups[4].Value;
                    
                    var speed = ParseSpeed(speedStr);
                    
                    progress = new DownloadProgress
                    {
                        TotalBytes = total,
                        DownloadedBytes = downloaded,
                        PercentComplete = percent,
                        Status = "Downloading",
                        ElapsedTime = elapsed,
                        DownloadSpeedBytesPerSecond = speed
                    };
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to parse aria2 output line: {Line}", line);
            }
            
            return false;
        }
        
        private static double ParseSpeed(string speedStr)
        {
            var match = Regex.Match(speedStr, @"(\d+(?:\.\d+)?)(B|KB|MB|GB)");
            if (!match.Success) return 0;
            
            var value = double.Parse(match.Groups[1].Value);
            var unit = match.Groups[2].Value;
            
            return unit switch
            {
                "B" => value,
                "KB" => value * 1024,
                "MB" => value * 1024 * 1024,
                "GB" => value * 1024 * 1024 * 1024,
                _ => value
            };
        }
    }
}