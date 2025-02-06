using System.Reflection;
using NLog;
using NLog.Config;

namespace PenumbraModForwarder.Common.Extensions;

public static class MergedSentryLogging
{
    public static void MergeSentryLogging(string sentryDsn, string applicationName)
    {
        if (string.IsNullOrWhiteSpace(sentryDsn))
        {
            Console.WriteLine("No Sentry DSN provided; skipping Sentry integration.");
            return;
        }
        
        var config = LogManager.Configuration ?? new LoggingConfiguration();
        
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        var semVersion = version == null
            ? "Local Build"
            : $"{version.Major}.{version.Minor}.{version.Build}";
        
        config.AddSentry(options =>
        {
            options.Dsn = sentryDsn;
            options.Layout = "${message}";
            options.BreadcrumbLayout = "${logger}: ${message}";
            options.MinimumBreadcrumbLevel = LogLevel.Debug;
            options.MinimumEventLevel = LogLevel.Error;
            options.AddTag("logger", "${logger}");
            options.Release = semVersion;
            options.AttachStacktrace = true; 
        });
        
        LogManager.Configuration = config;
        
        LogManager.ReconfigExistingLoggers();

        Console.WriteLine("Sentry logging merged successfully.");
    }
}