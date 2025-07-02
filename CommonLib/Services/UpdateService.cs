
using System.Net.Http.Headers;
using CommonLib.Extensions;
using CommonLib.Interfaces;
using CommonLib.Models;
using Newtonsoft.Json;
using NLog;

namespace CommonLib.Services;

public class UpdateService : IUpdateService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IConfigurationService _configurationService;
    private readonly HttpClient _httpClient;

    public UpdateService(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("CouncilOfTsukuyomi", "1.0"));
    }

    public class GitHubRelease
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; }

        [JsonProperty("prerelease")]
        public bool Prerelease { get; set; }

        [JsonProperty("assets")]
        public List<GitHubAsset> Assets { get; set; }

        [JsonProperty("body")]
        public string Body { get; set; } = string.Empty;

        [JsonProperty("published_at")]
        public DateTime PublishedAt { get; set; }
    }

    public class GitHubAsset
    {
        [JsonProperty("browser_download_url")]
        public string BrowserDownloadUrl { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public async Task<List<string>> GetUpdateZipLinksAsync(string currentVersion, string repository)
    {
        _logger.Debug("Entered `GetUpdateZipLinksAsync`. CurrentVersion: {CurrentVersion}, Repository: {Repository}", currentVersion, repository);

        var includePrerelease = (bool)_configurationService.ReturnConfigValue(c => c.Common.IncludePrereleases);
        _logger.Debug("IncludePrerelease: {IncludePrerelease}", includePrerelease);

        var latestRelease = await GetLatestReleaseAsync(includePrerelease, repository);
        if (latestRelease == null)
        {
            _logger.Debug("No GitHub releases found. Returning an empty list.");
            return new List<string>();
        }

        _logger.Debug("Latest release found: {TagName}. Checking if it is newer than current version.", latestRelease.TagName);

        if (IsVersionGreater(latestRelease.TagName, currentVersion))
        {
            _logger.Debug("A newer version is available: {TagName}. Current: {CurrentVersion}", latestRelease.TagName, currentVersion);

            var zipLinks = latestRelease.Assets
                .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.BrowserDownloadUrl)
                .ToList();

            _logger.Debug("Found {Count} .zip asset(s) in the latest release.", zipLinks.Count);
            return zipLinks;
        }

        _logger.Debug("Current version is up-to-date. Returning an empty list.");
        return new List<string>();
    }

    public async Task<bool> NeedsUpdateAsync(string currentVersion, string repository)
    {
        _logger.Debug("Entered `NeedsUpdateAsync`. CurrentVersion: {CurrentVersion}, Repository: {Repository}", currentVersion, repository);

        var includePrerelease = (bool)_configurationService.ReturnConfigValue(c => c.Common.IncludePrereleases);
        _logger.Debug("IncludePrerelease: {IncludePrerelease}", includePrerelease);

        var latestRelease = await GetLatestReleaseAsync(includePrerelease, repository);
        if (latestRelease == null)
        {
            _logger.Debug("No releases returned. No update needed.");
            return false;
        }

        var result = IsVersionGreater(latestRelease.TagName, currentVersion);
        _logger.Debug("`IsVersionGreater` returned {Result} for latest release {TagName}", result, latestRelease.TagName);

        return result;
    }

    public async Task<VersionInfo?> GetMostRecentVersionInfoAsync(string repository)
    {
        _logger.Debug("Entered `GetMostRecentVersionInfoAsync`. Repository: {Repository}", repository);

        var includePrerelease = (bool)_configurationService.ReturnConfigValue(c => c.Common.IncludePrereleases);
        _logger.Debug("IncludePrerelease: {IncludePrerelease}", includePrerelease);

        var latestRelease = await GetLatestReleaseAsync(includePrerelease, repository);
        if (latestRelease == null)
        {
            _logger.Debug("No releases found. Returning null.");
            return null;
        }

        _logger.Debug("Latest release version found: {TagName}", latestRelease.TagName);

        var version = latestRelease.TagName;
        if (latestRelease.Prerelease)
        {
            _logger.Debug("Release is a prerelease. Appending -b to version.");
            version = $"{latestRelease.TagName}-b";
        }

        var versionInfo = new VersionInfo
        {
            Version = version,
            Changelog = latestRelease.Body ?? string.Empty,
            IsPrerelease = latestRelease.Prerelease,
            PublishedAt = latestRelease.PublishedAt
        };

        // Parse the changelog to extract structured information
        versionInfo.ParseChangelog();

        _logger.Debug("Returning version info for {Version} with {ChangeCount} changes and {DownloadCount} downloads", 
            versionInfo.Version, versionInfo.Changes.Count, versionInfo.AvailableDownloads.Count);

        return versionInfo;
    }

    /// <summary>
    /// Gets all version information between the current version and the latest version
    /// </summary>
    /// <param name="currentVersion">The current version the user has</param>
    /// <param name="repository">The GitHub repository</param>
    /// <returns>List of VersionInfo for all versions newer than current, ordered from oldest to newest</returns>
    public async Task<List<VersionInfo>> GetAllVersionInfoSinceCurrentAsync(string currentVersion, string repository)
    {
        _logger.Debug("Entered `GetAllVersionInfoSinceCurrentAsync`. CurrentVersion: {CurrentVersion}, Repository: {Repository}", currentVersion, repository);

        var includePrerelease = (bool)_configurationService.ReturnConfigValue(c => c.Common.IncludePrereleases);
        _logger.Debug("IncludePrerelease: {IncludePrerelease}", includePrerelease);
        
        var allReleases = await GetAllReleasesAsync(includePrerelease, repository);
        if (allReleases == null || !allReleases.Any())
        {
            _logger.Debug("No releases found. Returning empty list.");
            return new List<VersionInfo>();
        }

        _logger.Debug("Found {Count} total releases. Filtering for versions newer than {CurrentVersion}", allReleases.Count, currentVersion);

        // Filter releases that are newer than current version
        var newerReleases = allReleases
            .Where(release => IsVersionGreater(release.TagName, currentVersion))
            .OrderBy(release => GetVersionSortKey(release.TagName))
            .ToList();

        _logger.Debug("Found {Count} releases newer than current version", newerReleases.Count);

        var versionInfoList = new List<VersionInfo>();
        foreach (var release in newerReleases)
        {
            var version = release.TagName;
            if (release.Prerelease)
            {
                version = $"{release.TagName}-b";
            }

            var versionInfo = new VersionInfo
            {
                Version = version,
                Changelog = release.Body ?? string.Empty,
                IsPrerelease = release.Prerelease,
                PublishedAt = release.PublishedAt
            };

            // Parse the changelog to extract structured information
            versionInfo.ParseChangelog();
            versionInfoList.Add(versionInfo);

            _logger.Debug("Added version info for {Version}", versionInfo.Version);
        }

        _logger.Debug("Returning {Count} version info objects", versionInfoList.Count);
        return versionInfoList;
    }

    /// <summary>
    /// Gets a consolidated changelog containing all changes since the current version
    /// </summary>
    /// <param name="currentVersion">The current version the user has</param>
    /// <param name="repository">The GitHub repository</param>
    /// <returns>A consolidated changelog string</returns>
    public async Task<string> GetConsolidatedChangelogSinceCurrentAsync(string currentVersion, string repository)
    {
        _logger.Debug("Entered `GetConsolidatedChangelogSinceCurrentAsync`. CurrentVersion: {CurrentVersion}, Repository: {Repository}", currentVersion, repository);

        var allVersions = await GetAllVersionInfoSinceCurrentAsync(currentVersion, repository);
        if (!allVersions.Any())
        {
            _logger.Debug("No newer versions found. Returning empty changelog.");
            return string.Empty;
        }

        var consolidatedChangelog = new System.Text.StringBuilder();
        foreach (var version in allVersions)
        {
            consolidatedChangelog.AppendLine($"## {version.Version}");
            consolidatedChangelog.AppendLine($"*Released: {version.PublishedAt:yyyy-MM-dd}*");
            consolidatedChangelog.AppendLine();
            consolidatedChangelog.AppendLine(version.Changelog);
            consolidatedChangelog.AppendLine();
        }

        var result = consolidatedChangelog.ToString().Trim();
        _logger.Debug("Generated consolidated changelog with {Length} characters covering {Count} versions", result.Length, allVersions.Count);
        
        return result;
    }

    /// <summary>
    /// Gets all releases from GitHub API (minimises API calls by fetching all at once)
    /// </summary>
    private async Task<List<GitHubRelease>?> GetAllReleasesAsync(bool includePrerelease, string repository)
    {
        _logger.Debug("Entered `GetAllReleasesAsync`. IncludePrerelease: {IncludePrerelease}, Repository: {Repository}", includePrerelease, repository);

        var allReleases = new List<GitHubRelease>();
        var page = 1;
        const int perPage = 100; // GitHub's maximum per page

        while (true)
        {
            var url = $"https://api.github.com/repos/{repository}/releases?page={page}&per_page={perPage}";
            _logger.Debug("Fetching releases page {Page}. URL: {Url}", page, url);

            using var response = await _httpClient.GetAsync(url);
            _logger.Debug("GitHub releases GET request for page {Page} completed with status code {StatusCode}", page, response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error("Request for releases page {Page} did not succeed with status {StatusCode}. Response: {ErrorContent}",
                    page, response.StatusCode, errorContent);
                break;
            }

            List<GitHubRelease>? pageReleases;
            try
            {
                pageReleases = await response.Content.ReadAsJsonAsync<List<GitHubRelease>>();
            }
            catch (JsonSerializationException ex)
            {
                var content = await response.Content.ReadAsStringAsync();
                _logger.Error(ex, "Error during JSON deserialization for page {Page}. Actual response: {Content}", page, content);
                break;
            }

            if (pageReleases == null || !pageReleases.Any())
            {
                _logger.Debug("No more releases found on page {Page}. Breaking pagination loop.", page);
                break;
            }

            allReleases.AddRange(pageReleases);
            _logger.Debug("Added {Count} releases from page {Page}. Total releases so far: {Total}", pageReleases.Count, page, allReleases.Count);

            // If we got less than the full page, we've reached the end
            if (pageReleases.Count < perPage)
            {
                _logger.Debug("Page {Page} returned fewer than {PerPage} releases. End of pagination reached.", page, perPage);
                break;
            }

            page++;
        }

        _logger.Debug("Fetched {Count} total releases across {Pages} pages", allReleases.Count, page);

        if (!includePrerelease)
        {
            var beforeFilter = allReleases.Count;
            allReleases = allReleases.Where(r => !r.Prerelease).ToList();
            _logger.Debug("Filtered out prereleases. Before: {Before}, After: {After}", beforeFilter, allReleases.Count);
        }

        return allReleases;
    }

    /// <summary>
    /// Creates a version sort key for proper semantic version ordering
    /// </summary>
    private (int major, int minor, int patch) GetVersionSortKey(string version)
    {
        var cleanVersion = version.StartsWith("v", StringComparison.OrdinalIgnoreCase) 
            ? version.Substring(1) 
            : version;

        var parts = cleanVersion.Split('.');
        if (parts.Length != 3)
        {
            // Fallback for non-standard version formats
            return (0, 0, 0);
        }

        var major = int.TryParse(parts[0], out var maj) ? maj : 0;
        var minor = int.TryParse(parts[1], out var min) ? min : 0;
        var patch = int.TryParse(parts[2], out var pat) ? pat : 0;

        return (major, minor, patch);
    }
    
    public async Task<string> GetMostRecentVersionAsync(string repository)
    {
        _logger.Debug("Entered `GetMostRecentVersionAsync`. Repository: {Repository}", repository);

        var versionInfo = await GetMostRecentVersionInfoAsync(repository);
        if (versionInfo == null)
        {
            _logger.Debug("No version info found. Returning empty string.");
            return string.Empty;
        }

        _logger.Debug("Latest release version found: {Version}", versionInfo.Version);
        return versionInfo.Version;
    }

    public async Task<GitHubRelease?> GetLatestReleaseAsync(bool includePrerelease, string repository)
    {
        _logger.Debug("Entered `GetLatestReleaseAsync`. IncludePrerelease: {IncludePrerelease}, Repository: {Repository}", includePrerelease, repository);

        var url = $"https://api.github.com/repos/{repository}/releases";
        _logger.Debug("Full releases URL: {Url}", url);

        using var response = await _httpClient.GetAsync(url);
        _logger.Debug("GitHub releases GET request completed with status code {StatusCode}", response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.Error("Request for releases did not succeed with status {StatusCode}. Response: {ErrorContent}",
                response.StatusCode, errorContent);
            return null;
        }

        List<GitHubRelease>? releases;
        try
        {
            releases = await response.Content.ReadAsJsonAsync<List<GitHubRelease>>();
        }
        catch (JsonSerializationException ex)
        {
            var content = await response.Content.ReadAsStringAsync();
            _logger.Error(ex, "Error during JSON deserialization. Actual response: {Content}", content);
            return null;
        }

        if (releases == null || releases.Count == 0)
        {
            _logger.Debug("No releases were deserialized or the list is empty.");
            return null;
        }

        _logger.Debug("Found {Count} releases. Filter prerelease: {FilterPrerelease}", releases.Count, !includePrerelease);

        var filtered = includePrerelease
            ? releases
            : releases.Where(r => !r.Prerelease).ToList();

        var latestRelease = filtered.FirstOrDefault();
        if (latestRelease == null)
        {
            _logger.Debug("No suitable release found after filtering prereleases.");
        }
        else
        {
            _logger.Debug("Using release with `tag_name` {TagName}", latestRelease.TagName);
        }

        return latestRelease;
    }

    private bool IsVersionGreater(string newVersion, string oldVersion)
    {
        _logger.Debug("`IsVersionGreater` check. New: {NewVersion}, Old: {OldVersion}", newVersion, oldVersion);

        if (string.IsNullOrWhiteSpace(newVersion) || string.IsNullOrWhiteSpace(oldVersion))
        {
            _logger.Debug("One or both version strings were null/empty. Returning false.");
            return false;
        }

        // Remove 'v' prefix if present
        var cleanNewVersion = newVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase) 
            ? newVersion.Substring(1) 
            : newVersion;
        var cleanOldVersion = oldVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase) 
            ? oldVersion.Substring(1) 
            : oldVersion;

        _logger.Debug("Cleaned versions. New: {CleanNewVersion}, Old: {CleanOldVersion}", cleanNewVersion, cleanOldVersion);

        var splittedNew = cleanNewVersion.Split('.');
        var splittedOld = cleanOldVersion.Split('.');
        
        if (splittedNew.Length != 3 || splittedOld.Length != 3)
        {
            _logger.Debug("Version not in x.x.x format. Using ordinal compare.");
            return string.CompareOrdinal(cleanNewVersion, cleanOldVersion) > 0;
        }

        if (!int.TryParse(splittedNew[0], out var majorNew) ||
            !int.TryParse(splittedNew[1], out var minorNew) ||
            !int.TryParse(splittedNew[2], out var patchNew))
        {
            _logger.Debug("Error parsing newVersion to integers. Using ordinal compare.");
            return string.CompareOrdinal(cleanNewVersion, cleanOldVersion) > 0;
        }

        if (!int.TryParse(splittedOld[0], out var majorOld) ||
            !int.TryParse(splittedOld[1], out var minorOld) ||
            !int.TryParse(splittedOld[2], out var patchOld))
        {
            _logger.Debug("Error parsing oldVersion to integers. Using ordinal compare.");
            return string.CompareOrdinal(cleanNewVersion, cleanOldVersion) > 0;
        }

        if (majorNew > majorOld) return true;
        if (majorNew < majorOld) return false;

        if (minorNew > minorOld) return true;
        if (minorNew < minorOld) return false;

        return patchNew > patchOld;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}