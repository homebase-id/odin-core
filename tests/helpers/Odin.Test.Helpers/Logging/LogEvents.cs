using NUnit.Framework;
using Serilog.Events;

namespace Odin.Test.Helpers.Logging;

public static class LogEvents
{
    public static void AssertEvents(Dictionary<LogEventLevel, List<LogEvent>> logEvents)
    {
        Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(0), Unexpected(LogEventLevel.Error, logEvents));
        Assert.That(logEvents[LogEventLevel.Fatal].Count, Is.EqualTo(0), Unexpected(LogEventLevel.Fatal, logEvents));
    }

    /// <summary>
    /// The failure message names the events: a teardown's console output often does not reach the CI log, and a
    /// bare count cannot tell which of several flakes fired.
    /// </summary>
    public static string Unexpected(LogEventLevel level, Dictionary<LogEventLevel, List<LogEvent>> logEvents)
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