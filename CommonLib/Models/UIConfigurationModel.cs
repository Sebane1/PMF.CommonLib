using System.ComponentModel.DataAnnotations;
using CommonLib.Attributes;

namespace CommonLib.Models;

public class UIConfigurationModel {
    [Display(Name = "Enable Notifications", GroupName = "Notification", Description = "Display Notifications")]
    public bool NotificationEnabled { get; set; } = true;

    [Display(Name = "Enable Notification Sound", GroupName = "Notification", Description = "Sound for Notifications")]
    public bool NotificationSoundEnabled { get; set; }
    [Display(Name = "Minimize To Tray", GroupName = "User Interface", Description = "Atomos will return to the tray when minimized.")]
    public bool MinimizeToTray { get; set; } = false;
    [Display(Name = "Close To Tray", GroupName = "User Interface", Description = "Atomos will continue running after closing the window and be available in the tray")]
    public bool CloseToTray { get; set; } = false;
    [Display(Name = "Show Notification Button", GroupName = "User Interface", Description = "Show the Notification Button")]
    public bool ShowNotificationButton { get; set; } = true;
    [ExcludeFromSettingsUI]
    public double NotificationButtonX { get; set; } = 20;
    [ExcludeFromSettingsUI]
    public double NotificationButtonY { get; set; } = 60;

}