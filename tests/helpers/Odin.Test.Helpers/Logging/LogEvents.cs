using NUnit.Framework;
using Serilog.Events;

namespace Odin.Test.Helpers.Logging;

public static class LogEvents
{
    public static void AssertEvents(Dictionary<LogEventLevel, List<LogEvent>> logEvents)
    {
        Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(0), "Unexpected number of Error log events");
        Assert.That(logEvents[LogEventLevel.Fatal].Count, Is.EqualTo(0), "Unexpected number of Fatal log events");
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
            Console.WriteLine("Begin log event");
            Console.WriteLine($"Message: {logEvent.RenderMessage()}");
            if (logEvent.Exception != null)
            {
                Console.WriteLine(logEvent.Exception);
            }
            Console.WriteLine("End log event");
        }
    }

}