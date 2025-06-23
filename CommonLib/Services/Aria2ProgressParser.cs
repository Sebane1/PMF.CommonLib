using System;
using System.Text.RegularExpressions;
using CommonLib.Models;
using NLog;

namespace CommonLib.Services;

public static class Aria2ProgressParser
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        
    // Store the last known speed and file size for completed downloads
    private static double _lastKnownSpeed = 0;
    private static long _lastKnownTotalBytes = 0;
    private static long _lastKnownDownloadedBytes = 0;
        
    public static bool TryParseProgressLine(string line, TimeSpan elapsed, out DownloadProgress? progress)
    {
        progress = null;
            
        if (string.IsNullOrWhiteSpace(line))
            return false;
            
        try
        {
            // Log all lines for debugging
            _logger.Trace("Parsing aria2 line: {Line}", line);
                
            // Pattern 0: Extract Content-Length from HTTP headers
            var contentLengthMatch = Regex.Match(line, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
            if (contentLengthMatch.Success)
            {
                var contentLength = long.Parse(contentLengthMatch.Groups[1].Value);
                _lastKnownTotalBytes = contentLength;
                    
                _logger.Info("Extracted Content-Length: {ContentLength} bytes ({Size} MB)", 
                    contentLength, contentLength / (1024.0 * 1024.0));
                    
                // Report initial progress with known file size
                progress = new DownloadProgress
                {
                    TotalBytes = contentLength,
                    DownloadedBytes = 0,
                    PercentComplete = 0,
                    Status = "Starting download",
                    ElapsedTime = elapsed,
                    DownloadSpeedBytesPerSecond = 0
                };
                    
                return true;
            }
                
            // Pattern 1: [#1 SIZE:123456/789012(15%) CN:1 DL:45678B ETA:12s]
            var progressMatch = Regex.Match(line, @"SIZE:(\d+)/(\d+)\((\d+)%\).*?DL:(\d+[KMGT]?B)", RegexOptions.IgnoreCase);
            if (progressMatch.Success)
            {
                var downloaded = long.Parse(progressMatch.Groups[1].Value);
                var total = long.Parse(progressMatch.Groups[2].Value);
                var percent = double.Parse(progressMatch.Groups[3].Value);
                var speedStr = progressMatch.Groups[4].Value;
                    
                var speed = ParseSpeed(speedStr);
                    
                // Cache the values
                _lastKnownSpeed = speed;
                _lastKnownTotalBytes = total;
                _lastKnownDownloadedBytes = downloaded;
                    
                progress = new DownloadProgress
                {
                    TotalBytes = total,
                    DownloadedBytes = downloaded,
                    PercentComplete = percent,
                    Status = "Downloading",
                    ElapsedTime = elapsed,
                    DownloadSpeedBytesPerSecond = speed
                };
                    
                _logger.Info("Successfully parsed SIZE format: {Percent}%, {Downloaded}/{Total} bytes, {Speed} B/s", 
                    percent, downloaded, total, speed);
                    
                return true;
            }
                
            // Pattern 2: Progress line with percentage - [#1 17% of 123MB DL:45MB ETA:12s]
            var progressMatch2 = Regex.Match(line, @"\[#\d+\s+(\d+)%\s+of\s+(\d+(?:\.\d+)?)(B|KB|MB|GB|TB).*?DL:(\d+(?:\.\d+)?)(B|KB|MB|GB|TB)", RegexOptions.IgnoreCase);
            if (progressMatch2.Success)
            {
                var percent = double.Parse(progressMatch2.Groups[1].Value);
                var totalValue = double.Parse(progressMatch2.Groups[2].Value);
                var totalUnit = progressMatch2.Groups[3].Value;
                var speedValue = double.Parse(progressMatch2.Groups[4].Value);
                var speedUnit = progressMatch2.Groups[5].Value;
                    
                var totalBytes = ConvertToBytes(totalValue, totalUnit);
                var downloadedBytes = (long)(totalBytes * percent / 100.0);
                var speed = ConvertToBytes(speedValue, speedUnit);
                    
                // Cache the values
                _lastKnownSpeed = speed;
                _lastKnownTotalBytes = (long)totalBytes;
                _lastKnownDownloadedBytes = downloadedBytes;
                    
                progress = new DownloadProgress
                {
                    TotalBytes = (long)totalBytes,
                    DownloadedBytes = downloadedBytes,
                    PercentComplete = percent,
                    Status = "Downloading",
                    ElapsedTime = elapsed,
                    DownloadSpeedBytesPerSecond = speed
                };
                    
                _logger.Info("Successfully parsed progress format 2: {Percent}%, {Downloaded}/{Total} bytes, {Speed} B/s", 
                    percent, downloadedBytes, totalBytes, speed);
                    
                return true;
            }
                
            // Pattern 3: Simple progress format with detailed stats - [#28acb8 0B/0B CN:1 DL:0B]
            var simpleProgressMatch = Regex.Match(line, @"\[#[a-f0-9]+\s+(\d+[KMGT]?B)/(\d+[KMGT]?B)\s+CN:\d+\s+DL:(\d+[KMGT]?B)", RegexOptions.IgnoreCase);
            if (simpleProgressMatch.Success)
            {
                var downloadedStr = simpleProgressMatch.Groups[1].Value;
                var totalStr = simpleProgressMatch.Groups[2].Value;
                var speedStr = simpleProgressMatch.Groups[3].Value;
                    
                var downloaded = ParseSize(downloadedStr);
                var total = ParseSize(totalStr);
                var speed = ParseSpeed(speedStr);
                    
                // If we got 0B/0B but we know the real total from Content-Length, use that
                if (total == 0 && _lastKnownTotalBytes > 0)
                {
                    total = _lastKnownTotalBytes;
                }
                    
                var percent = total > 0 ? (downloaded * 100.0 / total) : 0;
                    
                // Cache the values
                _lastKnownSpeed = speed;
                if (total > 0) _lastKnownTotalBytes = total;
                if (downloaded > _lastKnownDownloadedBytes) _lastKnownDownloadedBytes = downloaded;
                    
                progress = new DownloadProgress
                {
                    TotalBytes = total > 0 ? total : _lastKnownTotalBytes,
                    DownloadedBytes = downloaded,
                    PercentComplete = percent,
                    Status = downloaded > 0 ? "Downloading" : "Connecting",
                    ElapsedTime = elapsed,
                    DownloadSpeedBytesPerSecond = speed
                };
                    
                _logger.Info("Parsed simple progress: {Downloaded}/{Total} bytes ({Percent:F1}%), speed: {Speed} B/s", 
                    downloaded, total > 0 ? total : _lastKnownTotalBytes, percent, speed);
                    
                return true;
            }
                
            // Pattern 4: Simple DL speed - [#1 DL:45678B]
            var dlMatch = Regex.Match(line, @"DL:(\d+(?:\.\d+)?)(B|KB|MB|GB|TB)", RegexOptions.IgnoreCase);
            if (dlMatch.Success)
            {
                var speedValue = double.Parse(dlMatch.Groups[1].Value);
                var speedUnit = dlMatch.Groups[2].Value;
                var speed = ConvertToBytes(speedValue, speedUnit);
                    
                _lastKnownSpeed = speed;
                    
                progress = new DownloadProgress
                {
                    Status = "Downloading",
                    ElapsedTime = elapsed,
                    DownloadSpeedBytesPerSecond = speed,
                    TotalBytes = _lastKnownTotalBytes,
                    DownloadedBytes = _lastKnownDownloadedBytes
                };
                    
                _logger.Info("Parsed DL format: {Speed} B/s", speed);
                return true;
            }
                
            // Pattern 5: Look for percentage anywhere in aria2 progress lines - BUT ALSO EXTRACT SPEED
            var percentMatch = Regex.Match(line, @"\[#\d+.*?(\d+)%", RegexOptions.IgnoreCase);
            if (percentMatch.Success)
            {
                var percent = double.Parse(percentMatch.Groups[1].Value);
                
                // IMPORTANT: Also try to extract speed from this line!
                var speedInLine = 0.0;
                var speedMatch = Regex.Match(line, @"DL:(\d+(?:\.\d+)?)(KiB|MiB|GiB|KB|MB|GB|B)", RegexOptions.IgnoreCase);
                if (speedMatch.Success)
                {
                    var speedValue = double.Parse(speedMatch.Groups[1].Value);
                    var speedUnit = speedMatch.Groups[2].Value;
                    speedInLine = ConvertToBytes(speedValue, speedUnit);
                    _lastKnownSpeed = speedInLine; // Update cached speed
                    _logger.Info("Extracted speed from percentage line: {Speed} B/s ({SpeedValue} {SpeedUnit})", 
                        speedInLine, speedValue, speedUnit);
                }
                
                // Also try to extract size information
                var sizeMatch = Regex.Match(line, @"(\d+)MiB/(\d+)MiB", RegexOptions.IgnoreCase);
                if (sizeMatch.Success)
                {
                    var downloadedMiB = long.Parse(sizeMatch.Groups[1].Value);
                    var totalMiB = long.Parse(sizeMatch.Groups[2].Value);
                    
                    var extractedDownloadedBytes = downloadedMiB * 1024 * 1024;
                    var totalBytes = totalMiB * 1024 * 1024;
                    
                    _lastKnownDownloadedBytes = extractedDownloadedBytes;
                    _lastKnownTotalBytes = totalBytes;
                    
                    _logger.Info("Extracted size from percentage line: {Downloaded}MiB/{Total}MiB ({DownloadedBytes}/{TotalBytes} bytes)", 
                        downloadedMiB, totalMiB, extractedDownloadedBytes, totalBytes);
                }
                
                // Calculate bytes if we have total size
                var calculatedDownloadedBytes = _lastKnownTotalBytes > 0 ? (long)(_lastKnownTotalBytes * percent / 100.0) : _lastKnownDownloadedBytes;  // Changed variable name
                if (calculatedDownloadedBytes > _lastKnownDownloadedBytes)
                    _lastKnownDownloadedBytes = calculatedDownloadedBytes;
                    
                progress = new DownloadProgress
                {
                    PercentComplete = percent,
                    Status = "Downloading",
                    ElapsedTime = elapsed,
                    DownloadSpeedBytesPerSecond = speedInLine > 0 ? speedInLine : _lastKnownSpeed,
                    TotalBytes = _lastKnownTotalBytes,
                    DownloadedBytes = _lastKnownDownloadedBytes
                };
                    
                _logger.Info("Parsed percentage format: {Percent}%, speed: {Speed} B/s (extracted: {ExtractedSpeed}, cached: {CachedSpeed})", 
                    percent, speedInLine > 0 ? speedInLine : _lastKnownSpeed, speedInLine, _lastKnownSpeed);
                return true;
            }
                
            // Pattern 6: Final summary line with average speed - "9403dc|OK  |    71MiB/s|"
            var summaryMatch = Regex.Match(line, @"\|\s*(\d+(?:\.\d+)?)(B|KB|MB|GB|TB|MiB|KiB|GiB|TiB)/s\s*\|", RegexOptions.IgnoreCase);
            if (summaryMatch.Success)
            {
                var speedValue = double.Parse(summaryMatch.Groups[1].Value);
                var speedUnit = summaryMatch.Groups[2].Value;
                var speed = ConvertToBytes(speedValue, speedUnit);
                    
                _lastKnownSpeed = speed;
                    
                progress = new DownloadProgress
                {
                    PercentComplete = 100,
                    Status = "Completed",
                    ElapsedTime = elapsed,
                    DownloadSpeedBytesPerSecond = speed,
                    TotalBytes = _lastKnownTotalBytes,
                    DownloadedBytes = _lastKnownTotalBytes // When completed, downloaded = total
                };
                    
                _logger.Info("Parsed summary speed: {Speed} B/s", speed);
                return true;
            }
                
            // Pattern 7: Look for aria2 completion messages
            if (line.Contains("download completed", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Download complete", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("download was complete", StringComparison.OrdinalIgnoreCase))
            {
                progress = new DownloadProgress
                {
                    PercentComplete = 100,
                    Status = "Completed",
                    ElapsedTime = elapsed,
                    DownloadSpeedBytesPerSecond = _lastKnownSpeed,
                    TotalBytes = _lastKnownTotalBytes,
                    DownloadedBytes = _lastKnownTotalBytes // When completed, downloaded = total
                };
                    
                _logger.Debug("Detected completion message");
                return true;
            }
                
            // Pattern 8: Look for any speed information to cache for later use
            var anySpeedMatch = Regex.Match(line, @"(\d+(?:\.\d+)?)\s*(B|KB|MB|GB|TB|MiB|KiB|GiB|TiB)/s", RegexOptions.IgnoreCase);
            if (anySpeedMatch.Success)
            {
                var speedValue = double.Parse(anySpeedMatch.Groups[1].Value);
                var speedUnit = anySpeedMatch.Groups[2].Value;
                var speed = ConvertToBytes(speedValue, speedUnit);
                _lastKnownSpeed = speed;
                _logger.Debug("Cached speed from line: {Speed} B/s", speed);
            }
                
            // Log lines that might contain useful information for debugging
            if (line.Contains("gid:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("URI:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("STATUS:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("%") ||
                line.Contains("DL:") ||
                line.Contains("SIZE:") ||
                line.Contains("[#") ||
                line.Contains("downloading", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("/s") ||
                line.Contains("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("Line contains potential progress indicators: {Line}", line);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to parse aria2 output line: {Line}", line);
        }
            
        return false;
    }
        
    /// <summary>
    /// Reset cached values for a new download
    /// </summary>
    public static void Reset()
    {
        _lastKnownSpeed = 0;
        _lastKnownTotalBytes = 0;
        _lastKnownDownloadedBytes = 0;
        _logger.Debug("Aria2ProgressParser reset - cached values cleared");
    }
        
    private static long ParseSize(string sizeStr)
    {
        if (string.IsNullOrWhiteSpace(sizeStr))
            return 0;
                
        var match = Regex.Match(sizeStr, @"(\d+(?:\.\d+)?)(B|KB|MB|GB|TB|KiB|MiB|GiB|TiB)", RegexOptions.IgnoreCase);
        if (!match.Success) 
        {
            // Try without unit (assume bytes)
            var numberMatch = Regex.Match(sizeStr, @"(\d+(?:\.\d+)?)");
            if (numberMatch.Success)
            {
                return (long)double.Parse(numberMatch.Groups[1].Value);
            }
            return 0;
        }
            
        var value = double.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value;
            
        var result = (long)ConvertToBytes(value, unit);
            
        _logger.Debug("Parsed size {SizeStr} as {Result} bytes", sizeStr, result);
        return result;
    }
        
    private static double ConvertToBytes(double value, string unit)
    {
        return unit.ToUpperInvariant() switch
        {
            "B" => value,
            "KB" => value * 1024,
            "MB" => value * 1024 * 1024,
            "GB" => value * 1024 * 1024 * 1024,
            "TB" => value * 1024 * 1024 * 1024 * 1024,
            "KIB" => value * 1024,
            "MIB" => value * 1024 * 1024,
            "GIB" => value * 1024 * 1024 * 1024,
            "TIB" => value * 1024 * 1024 * 1024 * 1024,
            _ => value
        };
    }
        
    private static double ParseSpeed(string speedStr)
    {
        if (string.IsNullOrWhiteSpace(speedStr)) return 0;
    
        // Remove any extra whitespace and convert to uppercase for easier parsing
        var cleanSpeed = speedStr.Trim();
    
        // Handle binary units (KiB, MiB, GiB) and decimal units (KB, MB, GB)
        var regex = new Regex(@"(\d+(?:\.\d+)?)\s*(KiB|MiB|GiB|KB|MB|GB|B)?", RegexOptions.IgnoreCase);
        var match = regex.Match(cleanSpeed);
    
        if (!match.Success || !double.TryParse(match.Groups[1].Value, out var value))
            return 0;
    
        var unit = match.Groups[2].Value.ToUpper();
    
        return unit switch
        {
            "KIB" => value * 1024,           // Kibibyte (binary)
            "MIB" => value * 1024 * 1024,   // Mebibyte (binary)
            "GIB" => value * 1024 * 1024 * 1024, // Gibibyte (binary)
            "KB" => value * 1000,           // Kilobyte (decimal)
            "MB" => value * 1000 * 1000,   // Megabyte (decimal)
            "GB" => value * 1000 * 1000 * 1000, // Gigabyte (decimal)
            "B" or "" => value,             // Bytes
            _ => 0
        };
    }
}