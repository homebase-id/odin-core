using Microsoft.Extensions.Logging;
using Odin.Core.Logging.Statistics.Serilog;
using Serilog;
using Serilog.Events;

namespace Odin.Test.Helpers.Logging;

public static class TestLogFactory
{
    public static ILoggerFactory CreateLoggerFactory(
        ILogEventMemoryStore logEventMemoryStore,
        LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        return LoggerFactory.Create(builder =>
        {
            const string logOutputTemplate = "{Timestamp:HH:mm:ss.fff} {Level:u3} {Message:lj}{NewLine}{Exception}";
            var serilog = new LoggerConfiguration()
                .MinimumLevel.Is(minimumLevel)
                .WriteTo.Console(outputTemplate: logOutputTemplate)
                .WriteTo.Sink(new InMemorySink(logEventMemoryStore))
                .CreateLogger();
            builder.AddSerilog(serilog);
        });
    }

    public static ILogger<T> CreateConsoleLogger<T>(
        ILogEventMemoryStore logEventMemoryStore,
        LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        var loggerFactory = CreateLoggerFactory(logEventMemoryStore, minimumLevel);
        return loggerFactory.CreateLogger<T>();
    }
}