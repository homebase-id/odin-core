using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Test.Helpers.Logging;

namespace Odin.Core.Tests.Logging;

public class LogsAreVisibleInUnitTest
{
    [Test(Description = "Logs can be seen in console")]
    public void LogsCanBeSeenInConsole()
    {
        var logStore = new LogEventMemoryStore();
        var logger = TestLogFactory.CreateConsoleLogger<LogsAreVisibleInUnitTest>(logStore);
        logger.LogInformation("Hey you! Look for me in the console!");
        LogEvents.AssertEvents(logStore.GetLogEvents());
    }
}

