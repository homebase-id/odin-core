using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Odin.Test.Helpers.Logging;

public static class TestLogFactory
{
    public static ILogger<T> CreateConsoleLogger<T>(LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            const string logOutputTemplate = "{Timestamp:HH:mm:ss.fff} {Level:u3} {Message:lj}{NewLine}{Exception}";
            var serilog = new LoggerConfiguration()
                .MinimumLevel.Is(minimumLevel)
                .WriteTo.Console(outputTemplate: logOutputTemplate)
                .CreateLogger();
            builder.AddSerilog(serilog);
        });

        return loggerFactory.CreateLogger<T>();
    }
}