using System.Text.RegularExpressions;

namespace CommonLib.Models;

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
            
        // Enhanced regex patterns to match different formats including author:
        // 1. "Description (commit) by @username"
        // 2. "Description (#PR) (commit) by @username"
        // 3. "Description (commit)" - fallback without author
        // 4. "Description (#PR) (commit)" - fallback without author
            
        var commitPattern = @"\(([a-f0-9]{7,8})\)";
        var prPattern = @"\(#(\d+)\)";
        var authorPattern = @"by @(\w+)$";
            
        string description = content;
        string? commitHash = null;
        string? prNumber = null;
        string? author = null;
            
        // Extract author first (always at the end if present)
        var authorMatch = Regex.Match(content, authorPattern);
        if (authorMatch.Success)
        {
            author = authorMatch.Groups[1].Value;
            // Remove the author from the content for further parsing
            content = content.Substring(0, authorMatch.Index).Trim();
            description = content;
        }
            
        // Extract commit hash
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
            Author = author,
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
    public string? Author { get; set; }
    public string OriginalText { get; set; } = string.Empty;
        
    public bool HasCommitHash => !string.IsNullOrEmpty(CommitHash);
    public bool HasPullRequest => !string.IsNullOrEmpty(PullRequestNumber);
    public bool HasAuthor => !string.IsNullOrEmpty(Author);
        
    public string CommitUrl => HasCommitHash ? $"https://github.com/CouncilOfTsukuyomi/Atomos/commit/{CommitHash}" : string.Empty;
    public string PullRequestUrl => HasPullRequest ? $"https://github.com/CouncilOfTsukuyomi/Atomos/pull/{PullRequestNumber}" : string.Empty;
    public string AuthorUrl => HasAuthor ? $"https://github.com/{Author}" : string.Empty;
        
    public string AuthorAvatarUrl => HasAuthor ? $"https://github.com/{Author}.png" : string.Empty;
        
    // Alternative avatar sizes (GitHub supports these query parameters)
    public string GetAuthorAvatarUrl(int size = 64) => HasAuthor ? $"https://github.com/{Author}.png?size={size}" : string.Empty;
        
    public string GetCommitUrl(string repository = "CouncilOfTsukuyomi/Atomos")
    {
        return HasCommitHash ? $"https://github.com/{repository}/commit/{CommitHash}" : string.Empty;
    }
        
    public string GetPullRequestUrl(string repository = "CouncilOfTsukuyomi/Atomos")
    {
        return HasPullRequest ? $"https://github.com/{repository}/pull/{PullRequestNumber}" : string.Empty;
    }
        
    public string GetAuthorUrl(string repository = "CouncilOfTsukuyomi/Atomos")
    {
        return HasAuthor ? $"https://github.com/{Author}" : string.Empty;
    }
        
    // Clean display text without the full changelog formatting
    public string CleanDescription => Description;
        
    // Full display with optional technical details
    public string GetDisplayText(bool includeTechnicalDetails = false, bool includeAuthor = true)
    {
        var parts = new List<string> { Description };
            
        if (HasPullRequest && includeTechnicalDetails)
            parts.Add($"(#{PullRequestNumber})");
                
        if (HasCommitHash && includeTechnicalDetails)
            parts.Add($"({CommitHash})");
                
        if (HasAuthor && includeAuthor)
            parts.Add($"by @{Author}");
                
        return string.Join(" ", parts);
    }
        
    public string AuthorDisplayName => HasAuthor ? $"@{Author}" : string.Empty;
}
    
public class DownloadInfo
{
    public string Filename { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}