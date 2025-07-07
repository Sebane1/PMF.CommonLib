using System.Reflection;
using NLog;
using NLog.Config;
using Sentry.NLog;

namespace CommonLib.Extensions;

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
            options.SendDefaultPii = false;
        });
        
        LogManager.Configuration = config;
        
        LogManager.ReconfigExistingLoggers();

        Console.WriteLine("Sentry logging merged successfully.");
    }
    
    public static void DisableSentryLogging()
    {
        var config = LogManager.Configuration;
        if (config == null)
        {
            Console.WriteLine("No active NLog configuration found.");
            return;
        }
        
        if (config.AllTargets.FirstOrDefault(t => t is SentryTarget) is not SentryTarget sentryTarget)
        {
            Console.WriteLine("No Sentry target found in the NLog configuration.");
            return;
        }
        
        var rulesToRemove = config.LoggingRules
            .Where(r => r.Targets.Contains(sentryTarget))
            .ToList();
        foreach (var rule in rulesToRemove)
        {
            config.LoggingRules.Remove(rule);
        }
        
        config.RemoveTarget(sentryTarget.Name);
        
        LogManager.Configuration = config;
        LogManager.ReconfigExistingLoggers();

        Console.WriteLine("Sentry logging disabled.");
    }
}