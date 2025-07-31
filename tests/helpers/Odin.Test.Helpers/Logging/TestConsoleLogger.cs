using Microsoft.Extensions.Logging;
using Odin.Core.Logging.Statistics.Serilog;
using Serilog.Events;

namespace Odin.Test.Helpers.Logging;

public class TestConsoleLogger<T> : ILogger<T>
{
    private readonly ILogger<T> _innerLogger;

    public TestConsoleLogger(ILogEventMemoryStore store, LogEventLevel level = LogEventLevel.Debug)
    {
        var factory = TestLogFactory.CreateLoggerFactory(store, level);
        _innerLogger = factory.CreateLogger<T>();
    }

    IDisposable? ILogger.BeginScope<TState>(TState state)
    {
        return _innerLogger.BeginScope(state);
    }

    bool ILogger.IsEnabled(LogLevel logLevel)
    {
        return _innerLogger.IsEnabled(logLevel);
    }

    void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _innerLogger.Log(logLevel, eventId, state, exception, formatter);
    }
}
