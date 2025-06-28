
using NLog;
using NLog.Config;
using NLog.Targets;

namespace CommonLib.Extensions;

public static class MergedDebugLogging
{
    private const string DebugRuleName = "RuntimeDebugRule";
    
    public static void EnableDebugLogging()
    {
        var config = LogManager.Configuration;
        if (config == null)
        {
            Console.WriteLine("No active NLog configuration found.");
            return;
        }
        
        if (config.LoggingRules.Any(r => r.RuleName == DebugRuleName))
        {
            Console.WriteLine("Debug logging is already enabled.");
            return;
        }

        if (config.AllTargets.FirstOrDefault(t => t.Name == "console") is not ConsoleTarget consoleTarget)
        {
            Console.WriteLine("Console target not found in NLog configuration.");
            return;
        }
        
        var debugRule = new LoggingRule("*", LogLevel.Debug, LogLevel.Debug, consoleTarget)
        {
            RuleName = DebugRuleName
        };
        
        config.LoggingRules.Add(debugRule);
        
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
        
        var rulesToRemove = config.LoggingRules
            .Where(r => r.RuleName != null && r.RuleName.StartsWith(DebugRuleName))
            .ToList();
            
        if (!rulesToRemove.Any())
        {
            Console.WriteLine("Debug logging rules not found in the NLog configuration.");
            return;
        }
        
        foreach (var rule in rulesToRemove)
        {
            config.LoggingRules.Remove(rule);
        }
        
        LogManager.Configuration = config;
        LogManager.ReconfigExistingLoggers();

        Console.WriteLine("Debug logging disabled successfully.");
    }
}