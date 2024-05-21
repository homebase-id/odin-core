using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Test.Helpers.Logging;
using Serilog.Events;

namespace Odin.Core.Tests.Logging.Statistics.Serilog;

public class LogEventMemoryStoreTest
{
    [Test]
    public void LogStaticsAreCountingLogEvents()
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<LogsAreVisibleInUnitTest>(logStore);
        logger.LogInformation("One info event");
        logger.LogError("One error event");
        logger.LogError("A second error event");
        var logEvents = logStore.GetLogEvents();
        Assert.That(logEvents[LogEventLevel.Information].Count, Is.EqualTo(1), "Unexpected number of Information log events");
        Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(2), "Unexpected number of Error log events");
        Assert.That(logEvents[LogEventLevel.Fatal].Count, Is.EqualTo(0), "Unexpected number of Fatal log events");

        logStore.Clear();
        logEvents = logStore.GetLogEvents();
        Assert.That(logEvents[LogEventLevel.Information].Count, Is.EqualTo(0), "Unexpected number of Information log events");
        Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(0), "Unexpected number of Error log events");
    }
}