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
}