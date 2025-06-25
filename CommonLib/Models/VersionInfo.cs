using System.Text.RegularExpressions;

namespace CommonLib.Models
{
    public class VersionInfo
    {
        public string Version { get; set; } = string.Empty;
        public string Changelog { get; set; } = string.Empty;
        public bool IsPrerelease { get; set; }
        public DateTime PublishedAt { get; set; }
        
        // Parse the changes section specifically
        public List<ChangeEntry> Changes { get; private set; } = new();
        
        // Parse available downloads
        public List<DownloadInfo> AvailableDownloads { get; private set; } = new();
        
        // Get just the changes without the full changelog formatting
        public string ChangesOnly => string.Join("\n", Changes.Select(c => c.Description));
        
        // Parse the changelog when it's set
        public void ParseChangelog()
        {
            if (string.IsNullOrEmpty(Changelog)) return;
            
            ParseChanges();
            ParseDownloads();
        }
        
        private void ParseChanges()
        {
            Changes.Clear();
            
            var lines = Changelog.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            bool inChangesSection = false;
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                if (trimmedLine.StartsWith("## Changes", StringComparison.OrdinalIgnoreCase))
                {
                    inChangesSection = true;
                    continue;
                }
                
                if (trimmedLine.StartsWith("##") && inChangesSection)
                {
                    // We've reached another section, stop parsing changes
                    break;
                }
                
                if (inChangesSection && trimmedLine.StartsWith("-"))
                {
                    // Parse the change entry and extract commit info
                    var changeEntry = ParseChangeEntry(trimmedLine);
                    if (changeEntry != null)
                    {
                        Changes.Add(changeEntry);
                    }
                }
            }
        }
        
        private ChangeEntry? ParseChangeEntry(string line)
        {
            // Remove the leading dash and trim
            var content = line.Substring(1).Trim();
            
            // Regex patterns to match different formats:
            // 1. "Description (commit)" - e.g., "Update timeout for mods (ada284a)"
            // 2. "Description (#PR) (commit)" - e.g., "Update styles for plugin settings (#56) (ee74fa6)"
            // 3. Just "Description" if no commit hash is found
            
            var commitPattern = @"\(([a-f0-9]{7,8})\)$";
            var prPattern = @"\(#(\d+)\)";
            
            string description = content;
            string? commitHash = null;
            string? prNumber = null;
            
            // Extract commit hash (always at the end)
            var commitMatch = Regex.Match(content, commitPattern);
            if (commitMatch.Success)
            {
                commitHash = commitMatch.Groups[1].Value;
                // Remove the commit hash from the description
                description = content.Substring(0, commitMatch.Index).Trim();
            }
            
            // Extract PR number
            var prMatch = Regex.Match(description, prPattern);
            if (prMatch.Success)
            {
                prNumber = prMatch.Groups[1].Value;
                // Remove the PR reference from the description
                description = description.Replace(prMatch.Value, "").Trim();
            }
            
            // Clean up any extra whitespace
            description = Regex.Replace(description, @"\s+", " ").Trim();
            
            return new ChangeEntry
            {
                Description = description,
                CommitHash = commitHash,
                PullRequestNumber = prNumber,
                OriginalText = content
            };
        }
        
        private void ParseDownloads()
        {
            AvailableDownloads.Clear();
            
            var lines = Changelog.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            bool inDownloadsSection = false;
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                if (trimmedLine.StartsWith("## Available Downloads", StringComparison.OrdinalIgnoreCase))
                {
                    inDownloadsSection = true;
                    continue;
                }
                
                if (trimmedLine.StartsWith("##") && inDownloadsSection)
                {
                    break;
                }
                
                if (inDownloadsSection && trimmedLine.StartsWith("-"))
                {
                    // Parse download entries like: - `Atomos-Windows-x64.v1.0.2.zip` - Windows 64-bit (Universal)
                    var downloadInfo = ParseDownloadLine(trimmedLine);
                    if (downloadInfo != null)
                    {
                        AvailableDownloads.Add(downloadInfo);
                    }
                }
            }
        }
        
        private DownloadInfo? ParseDownloadLine(string line)
        {
            // Remove the leading dash and trim
            var content = line.Substring(1).Trim();
            
            // Look for pattern: `filename` - description
            var parts = content.Split(" - ", 2);
            if (parts.Length < 2) return null;
            
            var filename = parts[0].Trim('`', ' ');
            var description = parts[1].Trim();
            
            return new DownloadInfo
            {
                Filename = filename,
                Description = description
            };
        }
    }
    
    public class ChangeEntry
    {
        public string Description { get; set; } = string.Empty;
        public string? CommitHash { get; set; }
        public string? PullRequestNumber { get; set; }
        public string OriginalText { get; set; } = string.Empty;
        
        public bool HasCommitHash => !string.IsNullOrEmpty(CommitHash);
        public bool HasPullRequest => !string.IsNullOrEmpty(PullRequestNumber);
        
        public string CommitUrl => HasCommitHash ? $"https://github.com/CouncilOfTsukuyomi/Atomos/commit/{CommitHash}" : string.Empty;
        public string PullRequestUrl => HasPullRequest ? $"https://github.com/CouncilOfTsukuyomi/Atomos/pull/{PullRequestNumber}" : string.Empty;
        
        public string GetCommitUrl(string repository = "CouncilOfTsukuyomi/Atomos")
        {
            return HasCommitHash ? $"https://github.com/{repository}/commit/{CommitHash}" : string.Empty;
        }
        
        public string GetPullRequestUrl(string repository = "CouncilOfTsukuyomi/Atomos")
        {
            return HasPullRequest ? $"https://github.com/{repository}/pull/{PullRequestNumber}" : string.Empty;
        }
        
        // Clean display text without technical details
        public string CleanDescription => Description;
        
        // Full display with optional technical details
        public string GetDisplayText(bool includeTechnicalDetails = false)
        {
            if (!includeTechnicalDetails)
                return Description;
                
            var parts = new List<string> { Description };
            
            if (HasPullRequest)
                parts.Add($"(#{PullRequestNumber})");
                
            if (HasCommitHash)
                parts.Add($"({CommitHash})");
                
            return string.Join(" ", parts);
        }
    }
    
    public class DownloadInfo
    {
        public string Filename { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}