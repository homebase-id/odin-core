using Microsoft.Extensions.Logging;
using Odin.Core.Logging.Statistics.Serilog;
using Serilog;
using Serilog.Events;
using ILogger = Microsoft.Extensions.Logging.ILogger;

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

    //

    public static ILogger CreateConsoleLogger(
        string categoryName = "Test",
        LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        var logEventMemoryStore = new LogEventMemoryStore();
        return CreateConsoleLogger(logEventMemoryStore, categoryName, minimumLevel);
    }
    
    //
    
    public static ILogger<T> CreateConsoleLogger<T>(
        LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        var logEventMemoryStore = new LogEventMemoryStore();
        return CreateConsoleLogger<T>(logEventMemoryStore, minimumLevel);
    }
    
    //
    
    public static ILogger CreateConsoleLogger(
        ILogEventMemoryStore logEventMemoryStore,
        string categoryName = "Test",
        LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        var loggerFactory = CreateLoggerFactory(logEventMemoryStore, minimumLevel);
        return loggerFactory.CreateLogger(categoryName);
    }
    
    //

    public static ILogger<T> CreateConsoleLogger<T>(
        ILogEventMemoryStore logEventMemoryStore,
        LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        var loggerFactory = CreateLoggerFactory(logEventMemoryStore, minimumLevel);
        return loggerFactory.CreateLogger<T>();
    }

    //
}
