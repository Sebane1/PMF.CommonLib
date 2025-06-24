using System.ComponentModel.DataAnnotations;

namespace CommonLib.Models;

public class AdvancedConfigurationModel
{
    [Display(Name = "Penumbra Api Timeout (Seconds)", GroupName = "Penumbra", Description = "How long to wait for the Penumbra Api to respond")]
    public int PenumbraTimeOutInSeconds { get; set; } = 60;
    [Display(Name = "Show Watchdog Window", GroupName = "Advanced", Description = "Show the console application window for Watchdog")]
    public bool ShowWatchDogWindow { get; set; } = false;
    [Display(Name = "Enable Debug Logs", GroupName = "Advanced", Description = "Enable Debug Logs")]
    public bool EnableDebugLogs { get; set; }
}