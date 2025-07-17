using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Odin.Hosting.Cli;

#nullable enable

public class CommandLineLogFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "CommandLineLogFormatter";

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var logLevel = logEntry.LogLevel switch
        {
            LogLevel.Critical => "FTL",
            LogLevel.Error => "ERR",
            LogLevel.Warning => "WRN",
            LogLevel.Information => "INF",
            LogLevel.Debug => "DBG",
            LogLevel.Trace => "VRB",
            _ => logEntry.LogLevel.ToString().ToUpper()
        };

        var message = logEntry.Formatter(logEntry.State, null);

        if (logEntry.Exception != null)
        {
            var stackTrace = logEntry.Exception.StackTrace;
            var firstLine = stackTrace?.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            var exceptionInfo = $" - {logEntry.Exception.GetType().Name}: {logEntry.Exception.Message}";
            var stackInfo = !string.IsNullOrEmpty(firstLine) ? $" ({firstLine})" : "";
            textWriter.WriteLine($"[{logLevel}] {message}{exceptionInfo}{stackInfo}");
        }
        else
        {
            textWriter.WriteLine($"[{logLevel}] {message}");
        }
    }
}

//

public static class CommandLineLoggerExtensions
{
    public static IServiceCollection AddCommandLineLogging(
        this IServiceCollection serviceCollection,
        LogLevel minimumLevel = LogLevel.Debug)
    {
        return serviceCollection.AddLogging(builder =>
        {
            builder
                .ClearProviders()
                .SetMinimumLevel(minimumLevel)
                .AddConsole(options => options.FormatterName = CommandLineLogFormatter.FormatterName)
                .AddConsoleFormatter<CommandLineLogFormatter, ConsoleFormatterOptions>();
        });
    }
}