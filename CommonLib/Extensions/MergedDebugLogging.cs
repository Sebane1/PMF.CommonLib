
using NLog;
using NLog.Config;

namespace CommonLib.Extensions;

public static class MergedDebugLogging
{
    private static readonly List<LoggingRule> _originalRules = new();
    
    public static void EnableDebugLogging()
    {
        var config = LogManager.Configuration;
        if (config == null)
        {
            Console.WriteLine("No active NLog configuration found.");
            return;
        }
        
        if (_originalRules.Any())
        {
            Console.WriteLine("Debug logging is already enabled.");
            return;
        }
        
        _originalRules.AddRange(config.LoggingRules.ToList());
        
        config.LoggingRules.Clear();
        
        var consoleTarget = config.AllTargets.FirstOrDefault(t => t.Name == "console");
        var fileTarget = config.AllTargets.FirstOrDefault(t => t.Name == "file");
        
        if (consoleTarget != null)
        {
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, consoleTarget);
        }
        
        if (fileTarget != null)
        {
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);
        }
        
        LogManager.Configuration = config;
        LogManager.ReconfigExistingLoggers();

        Console.WriteLine("Debug logging enabled successfully.");
    }
    
    public static void DisableDebugLogging()
    {
        var config = LogManager.Configuration;
        if (config == null)
        {
            Console.WriteLine("No active NLog configuration found.");
            return;
        }
        
        if (!_originalRules.Any())
        {
            Console.WriteLine("Debug logging was not enabled or original rules not found.");
            return;
        }
        
        // Restore the original rules
        config.LoggingRules.Clear();
        foreach (var rule in _originalRules)
        {
            config.LoggingRules.Add(rule);
        }
        
        _originalRules.Clear();
        
        LogManager.Configuration = config;
        LogManager.ReconfigExistingLoggers();

        Console.WriteLine("Debug logging disabled successfully.");
    }
}