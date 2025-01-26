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

    private static readonly string _cacheFilePath = Path.Combine(
        ConfigurationConsts.CachePath,
        "xivmodarchive",
        "mods.cache"
    );

    private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(30);

    public XmaModDisplay()
    {
        var directory = Path.GetDirectoryName(_cacheFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Fetches and combines results from page 1 and page 2 of the "time_published" descending search,
    /// returning a list of distinct mods by ImageUrl.
    /// </summary>
    public async Task<List<XmaMods>> GetRecentMods()
    {
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

        var page1Results = await ParsePageAsync(1);
        var page2Results = await ParsePageAsync(2);

        // Combine and deduplicate mods by ImageUrl
        var distinctMods = page1Results
            .Concat(page2Results)
            .GroupBy(m => m.ImageUrl)
            .Select(g => g.First())
            .ToList();

        // Write cache to file with a new expiration time
        var newCache = new XmaCacheData
        {
            Mods = distinctMods,
            ExpirationTime = DateTimeOffset.Now.Add(_cacheDuration)
        };

        SaveCacheToFile(newCache);

        return distinctMods;
    }

    /// <summary>
    /// Parses a single search-results page from XIV Mod Archive.
    /// This function extracts the mod name, publisher, type, image URL, direct link, and the gender
    /// </summary>
    /// <param name="pageNumber">Which page number to fetch (1, 2, etc.).</param>
    /// <returns>A list of parsed XmaMods.</returns>
    private async Task<List<XmaMods>> ParsePageAsync(int pageNumber)
    {
        var url = $"https://www.xivmodarchive.com/search?sortby=time_published&sortorder=desc&dt_compat=1&page={pageNumber}";
        const string domain = "https://www.xivmodarchive.com";

        using var client = new HttpClient();
        var html = await client.GetStringAsync(url);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var results = new List<XmaMods>();

        // Look for <div class="mod-card"> that might contain the data, including Genders.
        var modCards = doc.DocumentNode.SelectNodes("//div[contains(@class, 'mod-card')]");
        if (modCards == null)
        {
            _logger.Debug("No mod-card blocks found for page {PageNumber}.", pageNumber);
            return results;
        }

        foreach (var modCard in modCards)
        {
            // The detail link is often in an anchor child of the card
            var linkNode = modCard.SelectSingleNode(".//a[@href]");
            var linkAttr = linkNode?.GetAttributeValue("href", "") ?? "";
            var fullLink = string.IsNullOrWhiteSpace(linkAttr) ? "" : domain + linkAttr;

            // The mod name is usually found in <h5 class="card-title"> or similar
            var nameNode = modCard.SelectSingleNode(".//h5[contains(@class,'card-title')]");
            var rawName = nameNode?.InnerText?.Trim() ?? "";
            var normalizedName = NormalizeModName(rawName);

            // The publisher text (e.g., "By: SomeName") is typically in:
            // <p class="card-text mx-2">By: <a href="/user/...">Publisher</a></p>
            var publisherNode = modCard.SelectSingleNode(".//p[contains(@class,'card-text')]/a[@href]");
            var publisherText = publisherNode?.InnerText?.Trim() ?? "";

            // The 'Type' and 'Genders' appear in <code class="text-light"> lines
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

            // Determine the gender based on the text
            var genderVal = XmaGender.Unisex;
            if (string.Equals(genderText, "male", StringComparison.OrdinalIgnoreCase))
            {
                genderVal = XmaGender.Male; 
                
            } else if (string.Equals(genderText, "female", StringComparison.OrdinalIgnoreCase))
            {
                genderVal = XmaGender.Female;
            }

            // The image is the 'card-img-top' <img> node
            var imgNode = modCard.SelectSingleNode(".//img[contains(@class, 'card-img-top')]");
            var imgUrl = imgNode?.GetAttributeValue("src", "") ?? "";

            // Skip if no image is found, or if there's no link
            if (string.IsNullOrWhiteSpace(imgUrl))
            {
                _logger.Warn("Skipping mod due to missing image URL, Name={Name}", normalizedName);
                continue;
            }

            // Build final object
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
    /// </summary>
    /// <param name="modUrl">The URL to the mod's detail page.</param>
    /// <returns>The direct download link if found, otherwise null.</returns>
    public async Task<string?> GetModDownloadLinkAsync(string modUrl)
    {
        try
        {
            const string domain = "https://www.xivmodarchive.com";
            if (!modUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                modUrl = domain + modUrl;

            using var client = new HttpClient();
            var html = await client.GetStringAsync(modUrl);

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

            // Decode HTML entities (e.g. &#39; => ')
            hrefValue = WebUtility.HtmlDecode(hrefValue);

            // If the URL is relative, prepend the domain
            if (hrefValue.StartsWith("/"))
                hrefValue = domain + hrefValue;

            // Unescape existing percent-encoded sequences (e.g. %2520 => %20)
            hrefValue = Uri.UnescapeDataString(hrefValue);

            // Manually encode any remaining literal spaces/apostrophes
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
    /// Normalizes mod names to ensure compatibility with Avalonia.
    /// Decodes HTML-encoded characters, removes non-ASCII characters,
    /// trims whitespace, and replaces problematic characters.
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