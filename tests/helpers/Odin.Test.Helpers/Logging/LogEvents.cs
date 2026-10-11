using NUnit.Framework;
using Serilog.Events;

namespace Odin.Test.Helpers.Logging;

public static class LogEvents
{
    public static void AssertEvents(Dictionary<LogEventLevel, List<LogEvent>> logEvents)
    {
        AssertCount(logEvents, LogEventLevel.Error, 0);
        AssertCount(logEvents, LogEventLevel.Fatal, 0);
    }

    /// <summary>
    /// The failure message names the events: a teardown's console output often does not reach the CI log, and a
    /// bare count cannot tell which of several flakes fired.
    /// </summary>
    public static void AssertCount(Dictionary<LogEventLevel, List<LogEvent>> logEvents, LogEventLevel level, int expected)
    {
        Assert.That(logEvents[level].Count, Is.EqualTo(expected), () => Unexpected(level, logEvents));
    }

    private static string Unexpected(LogEventLevel level, Dictionary<LogEventLevel, List<LogEvent>> logEvents)
    {
        var events = logEvents[level];
        var lines = events.Select(e => $"  {e.Timestamp:HH:mm:ss.fff} {e.RenderMessage()}" +
                                       (e.Exception == null ? "" : $" [{e.Exception.GetType().Name}: {e.Exception.Message}]"));
        return $"Unexpected number of {level} log events:\n{string.Join("\n", lines)}";
    }

    public static void AssertLogMessageExists(IEnumerable<LogEvent> logEvents, string message)
    {
        var found = logEvents.Any(e => e.RenderMessage() == message);
        Assert.That(found, Is.True, $"Expected log message not found: '{message}'");
    }

    public static void DumpEvents(List<LogEvent> logEvents)
    {
        foreach (var logEvent in logEvents)
        {
            Console.WriteLine("Begin LogEvent dump");
            Console.WriteLine($"Message: {logEvent.RenderMessage()}");
            if (logEvent.Exception != null)
            {
                Console.WriteLine(logEvent.Exception);
            }
            Console.WriteLine("End LogEvent dump");
        }
    }

    public static void DumpErrorEvents(Dictionary<LogEventLevel, List<LogEvent>> logEvents)
    {
        DumpEvents(logEvents[LogEventLevel.Error]);
        DumpEvents(logEvents[LogEventLevel.Fatal]);
    }

}