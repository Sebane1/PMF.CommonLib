using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using PenumbraModForwarder.Common.Consts;
using LogLevel = NLog.LogLevel;

namespace PenumbraModForwarder.Common.Extensions;

public static class Logging
{
    private static LoggingConfiguration CreateBaseConfiguration(string applicationName)
    {
        // Create a daily subfolder named "yyyy-MM-dd" underneath the logs path.
        var dailyFolder = Path.Combine(ConfigurationConsts.LogsPath, DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dailyFolder);

        var config = new LoggingConfiguration();

        var consoleTarget = new ConsoleTarget("console")
        {
            Layout = "[${longdate} ${level:uppercase=true}] [${logger}] ${message}${exception}"
        };

#if DEBUG
        // In Debug mode, include all log levels in console.
        config.AddRuleForAllLevels(consoleTarget);
#else
            // In Release mode, filter out Debug (and Trace) level logs on console.
            config.AddRule(LogLevel.Info, LogLevel.Warn, consoleTarget);
#endif

        // Configure the file target to store logs in the daily folder
        var fileTarget = new FileTarget("file")
        {
            FileName = Path.Combine(dailyFolder, $"{applicationName}.log"),
            ArchiveFileName = Path.Combine(dailyFolder, $"{applicationName}.{{#}}.log"),
            ArchiveNumbering = ArchiveNumberingMode.Rolling,
            ArchiveEvery = FileArchivePeriod.Day,
            MaxArchiveFiles = 7,
            Layout = "[${longdate} ${level:uppercase=true}] [${logger}] ${message}${exception}"
        };
        config.AddTarget(fileTarget);
        config.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget);

        return config;
    }

    public static void ConfigureLogging(IServiceCollection services, string applicationName)
    {
        Directory.CreateDirectory(ConfigurationConsts.LogsPath);

        var config = CreateBaseConfiguration(applicationName);
        LogManager.Configuration = config;

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            builder.AddNLog();
        });
    }

    public static void EnableSentry(string sentryDsn, string applicationName)
    {
        if (string.IsNullOrWhiteSpace(sentryDsn))
        {
            Console.WriteLine("Sentry DSN not provided. Skipping Sentry enablement.");
            return;
        }

        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        var semVersion = version == null
            ? "Local Build"
            : $"{version.Major}.{version.Minor}.{version.Build}";

        var config = CreateBaseConfiguration(applicationName);

        config.AddSentry(options =>
        {
            options.Dsn = sentryDsn;
            options.Layout = "${message}";
            options.BreadcrumbLayout = "${logger}: ${message}";
            options.MinimumBreadcrumbLevel = LogLevel.Debug;
            options.MinimumEventLevel = LogLevel.Error;
            options.AddTag("logger", "${logger}");
            options.Release = semVersion;
            // Keep exception details
            options.AttachStacktrace = true;
        });

        LogManager.Configuration = config;
        Console.WriteLine("Sentry is now enabled at runtime.");
    }

    public static void DisableSentry(string applicationName)
    {
        var config = CreateBaseConfiguration(applicationName);
        LogManager.Configuration = config;
        Console.WriteLine("Sentry has been disabled and removed from the logger.");
    }
}