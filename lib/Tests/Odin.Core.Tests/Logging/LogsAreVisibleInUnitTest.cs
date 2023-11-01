using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Test.Helpers.Logging;

namespace Odin.Core.Tests.Logging;

public class LogsAreVisibleInUnitTest
{
    [Test(Description = "Logs can be seen in console")]
    public void LogsCanBeSeenInConsole()
    {
        var logger = TestLogFactory.CreateConsoleLogger<LogsAreVisibleInUnitTest>();
        logger.LogInformation("Hey you! Look for me in the console!");
        Assert.True(true);
    }
}

