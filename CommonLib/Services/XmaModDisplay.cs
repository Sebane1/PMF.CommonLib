using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using MessagePack;
using NLog;
using PenumbraModForwarder.Common.Consts;
using PenumbraModForwarder.Common.Enums;
using PenumbraModForwarder.Common.Interfaces;
using PenumbraModForwarder.Common.Models;

namespace PenumbraModForwarder.Common.Services;

public class XmaModDisplay : IXmaModDisplay
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly IConfigurationService _configurationService;
    private readonly IXmaHttpClientFactory _httpClientFactory;

    // We store the last known cookie to detect changes between calls.
    private string? _lastCookieValue;

    // Caching and file-based storage logic.
    private static readonly string _cacheFilePath = Path.Combine(
        ConfigurationConsts.CachePath,
        "xivmodarchive",
        "mods.cache"
    );
    private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);

    public XmaModDisplay(
        IConfigurationService configurationService,
        IXmaHttpClientFactory httpClientFactory
    )
    {
        _configurationService = configurationService;
        _httpClientFactory = httpClientFactory;

        // Ensure the cache directory exists
        var directory = Path.GetDirectoryName(_cacheFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Initialize _lastCookieValue to the current cookie from config
        // so that we can detect when it changes later.
        _lastCookieValue = GetCurrentCookieValue();
    }

    /// <summary>
    /// Retrieves and merges results from page 1 and page 2 of the time_published descending search,
    /// returning a list of distinct mods by ImageUrl. If the cookie changes before or during fetch,
    /// the cached data is invalidated to ensure correct access if the old cookie was expired.
    /// </summary>
    public async Task<List<XmaMods>> GetRecentMods()
    {
        // If the cookie changed, invalidate the existing cache data.
        InvalidateCacheOnCookieChange();

        var cachedData = LoadCacheFromFile();
        if (cachedData != null && cachedData.ExpirationTime > DateTimeOffset.Now)
        {
            _logger.Debug(
                "Returning persisted cache. Valid until {ExpirationTime}.",
                cachedData.ExpirationTime.ToString("u")
            );
            return cachedData.Mods;
        }

        _logger.Debug("Cache is empty or expired. Fetching new data...");

        // Create (or reuse) the HttpClient from the factory.
        var httpClient = _httpClientFactory.CreateClient();

        var page1Results = await ParsePageAsync(1, httpClient);
        var page2Results = await ParsePageAsync(2, httpClient);

        // Combine and deduplicate mods by ImageUrl
        var distinctMods = page1Results
            .Concat(page2Results)
            .GroupBy(m => m.ImageUrl)
            .Select(g => g.First())
            .ToList();

        // Write cache to file with a fresh expiration
        var newCache = new XmaCacheData
        {
            Mods = distinctMods,
            ExpirationTime = DateTimeOffset.Now.Add(_cacheDuration)
        };

        SaveCacheToFile(newCache);

        return distinctMods;
    }

    /// <summary>
    /// Parses a single search-results page from XIV Mod Archive using the provided HttpClient.
    /// Extracts mod name, publisher, type, image URL, direct link, and gender info.
    /// </summary>
    private async Task<List<XmaMods>> ParsePageAsync(int pageNumber, HttpClient httpClient)
    {
        var url = $"https://www.xivmodarchive.com/search?sortby=time_published&sortorder=desc&dt_compat=1&page={pageNumber}";
        const string domain = "https://www.xivmodarchive.com";

        var html = await httpClient.GetStringAsync(url);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var results = new List<XmaMods>();
        var modCards = doc.DocumentNode.SelectNodes("//div[contains(@class, 'mod-card')]");

        if (modCards == null)
        {
            _logger.Debug("No mod-card blocks found for page {PageNumber}.", pageNumber);
            return results;
        }

        foreach (var modCard in modCards)
        {
            // The detail link is often in <a href="...">
            var linkNode = modCard.SelectSingleNode(".//a[@href]");
            var linkAttr = linkNode?.GetAttributeValue("href", "") ?? "";
            var fullLink = string.IsNullOrWhiteSpace(linkAttr) ? "" : domain + linkAttr;

            // Name in <h5 class="card-title">
            var nameNode = modCard.SelectSingleNode(".//h5[contains(@class,'card-title')]");
            var rawName = nameNode?.InnerText?.Trim() ?? "";
            var normalizedName = NormalizeModName(rawName);

            // Publisher text in <p class="card-text mx-2"> or similar
            var publisherNode = modCard.SelectSingleNode(".//p[contains(@class,'card-text')]/a[@href]");
            var publisherText = publisherNode?.InnerText?.Trim() ?? "";

            // Type/genders in <code class="text-light">
            var infoNodes = modCard.SelectNodes(".//code[contains(@class, 'text-light')]");
            var typeText = "";
            var genderText = "";

            if (infoNodes != null)
            {
                foreach (var node in infoNodes)
                {
                    var text = node.InnerText.Trim();
                    if (text.StartsWith("Type:", StringComparison.OrdinalIgnoreCase))
                    {
                        typeText = text.Replace("Type:", "").Trim();
                    }
                    else if (text.StartsWith("Genders:", StringComparison.OrdinalIgnoreCase))
                    {
                        genderText = text.Replace("Genders:", "").Trim().ToLowerInvariant();
                    }
                }
            }

            // Convert to enum
            var genderVal = XmaGender.Unisex;
            if (string.Equals(genderText, "male", StringComparison.OrdinalIgnoreCase))
            {
                genderVal = XmaGender.Male;
            }
            else if (string.Equals(genderText, "female", StringComparison.OrdinalIgnoreCase))
            {
                genderVal = XmaGender.Female;
            }

            // The image is in 'card-img-top' <img>
            var imgNode = modCard.SelectSingleNode(".//img[contains(@class, 'card-img-top')]");
            var imgUrl = imgNode?.GetAttributeValue("src", "") ?? "";

            // Skip mods without an image
            if (string.IsNullOrWhiteSpace(imgUrl))
            {
                _logger.Warn("Skipping mod due to missing image URL, Name={Name}", normalizedName);
                continue;
            }

            var mod = new XmaMods
            {
                Name = normalizedName,
                Publisher = publisherText,
                Type = typeText,
                ImageUrl = imgUrl,
                ModUrl = fullLink,
                Gender = genderVal
            };

            results.Add(mod);
        }

        return results;
    }

    /// <summary>
    /// Loads the mod details page and attempts to parse the direct download link.
    /// Using the factory ensures the latest cookie is included.
    /// </summary>
    public async Task<string?> GetModDownloadLinkAsync(string modUrl)
    {
        // Check for cookie changes before making a request.
        InvalidateCacheOnCookieChange();
        var httpClient = _httpClientFactory.CreateClient();

        try
        {
            const string domain = "https://www.xivmodarchive.com";
            if (!modUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                modUrl = domain + modUrl;
            }

            var html = await httpClient.GetStringAsync(modUrl);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var downloadNode = doc.DocumentNode.SelectSingleNode("//a[@id='mod-download-link']");
            if (downloadNode == null)
            {
                _logger.Warn("No download anchor found on: {ModUrl}", modUrl);
                return null;
            }

            var hrefValue = downloadNode.GetAttributeValue("href", "");
            if (string.IsNullOrWhiteSpace(hrefValue))
            {
                _logger.Warn("Download link was empty or missing on: {ModUrl}", modUrl);
                return null;
            }

            // Decode HTML entities
            hrefValue = WebUtility.HtmlDecode(hrefValue);

            // If the link is relative, prepend domain
            if (hrefValue.StartsWith("/"))
            {
                hrefValue = domain + hrefValue;
            }

            // Unescape percent-encoded sequences
            hrefValue = Uri.UnescapeDataString(hrefValue);

            // Manually encode spaces/apostrophes
            hrefValue = hrefValue
                .Replace(" ", "%20")
                .Replace("'", "%27");

            return hrefValue;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to parse mod download link from: {ModUrl}", modUrl);
            return null;
        }
    }

    /// <summary>
    /// Deletes the existing mod cache file if the connect.sid cookie has changed.
    /// Then updates <see cref="_lastCookieValue"/> to the current cookie.
    /// </summary>
    private void InvalidateCacheOnCookieChange()
    {
        var currentCookie = GetCurrentCookieValue();
        if (currentCookie != _lastCookieValue)
        {
            _logger.Debug("Cookie changed. Invalidating cached data.");

            // Remove any existing cache file
            if (File.Exists(_cacheFilePath))
            {
                try
                {
                    File.Delete(_cacheFilePath);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to delete old cache file while invalidating cache.");
                }
            }

            _lastCookieValue = currentCookie;
        }
    }

    /// <summary>
    /// Fetches the current connect.sid cookie value from IConfigurationService.
    /// </summary>
    private string GetCurrentCookieValue()
    {
        return (string)_configurationService.ReturnConfigValue(c => c.AdvancedOptions.XIVModArchiveCookie);
    }

    /// <summary>
    /// Decodes HTML entities, removes non-ASCII, trims whitespace,
    /// and replaces multiline breaks for a consistent mod name.
    /// </summary>
    private string NormalizeModName(string name)
    {
        var decoded = WebUtility.HtmlDecode(name);
        var asciiOnly = string.Concat(decoded.Where(c => c <= 127));
        var sanitized = asciiOnly
            .Replace("\\n", " ")
            .Replace("\\r", " ")
            .Replace("\\t", " ");

        var normalized = Regex
            .Replace(sanitized, "\\s+", " ")
            .Trim();

        return normalized;
    }

    private XmaCacheData? LoadCacheFromFile()
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
                return null;

            var bytes = File.ReadAllBytes(_cacheFilePath);
            return MessagePackSerializer.Deserialize<XmaCacheData>(bytes);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load cache from file.");
            return null;
        }
    }

    private void SaveCacheToFile(XmaCacheData data)
    {
        try
        {
            var bytes = MessagePackSerializer.Serialize(data);
            File.WriteAllBytes(_cacheFilePath, bytes);

            _logger.Debug(
                "Cache saved to {FilePath}, valid until {ExpirationTime}.",
                _cacheFilePath,
                data.ExpirationTime.ToString("u")
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save cache to file.");
        }
    }
}