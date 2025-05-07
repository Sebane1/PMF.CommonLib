using CommonLib.Enums;
using CommonLib.Models;

namespace CommonLib.Interfaces;

public interface IStatisticService
{ 
    Task IncrementStatAsync(Stat stat);
    Task<int> GetStatCountAsync(Stat stat);
    Task RecordModInstallationAsync(string modName);
    Task<ModInstallationRecord?> GetMostRecentModInstallationAsync();
    Task<int> GetUniqueModsInstalledCountAsync();
    Task<List<ModInstallationRecord>> GetAllInstalledModsAsync();
    Task<int> GetModsInstalledTodayAsync();
}