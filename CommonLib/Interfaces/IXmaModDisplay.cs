using CommonLib.Models;

namespace CommonLib.Interfaces;

public interface IXmaModDisplay
{
    Task<List<XmaMods>> GetRecentMods();
    Task<string?> GetModDownloadLinkAsync(string modUrl);
}