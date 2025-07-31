using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Test.Helpers.Logging;
using Serilog.Events;

namespace Odin.Test.Helpers.Tests.Logging;

public class TestConsoleLoggerTests
{
    [Test]
    public void ItShouldLookupErrorsWithoutDi()
    {
        // Arrange
        var store = new LogEventMemoryStore();
        var logger = new TestConsoleLogger<TestConsoleLoggerTests>(store);

        // Act
        logger.LogInformation("info");
        logger.LogError("error");

        // Assert
        var errorLogs = store.GetLogEvents()[LogEventLevel.Error];
        Assert.That(errorLogs.Count, Is.EqualTo(1));
        var found = errorLogs.Any(e => e.RenderMessage().Contains("error"));
        Assert.That(found, Is.True);
    }

    [Test]
    public void ItShouldLookupErrorsWithDi()
    {
        // Arrange
        var builder = new ContainerBuilder();

        builder.RegisterGeneric(typeof(TestConsoleLogger<>)).As(typeof(ILogger<>)).SingleInstance();
        builder.RegisterModule(new LoggingAutofacModule());

        var services = builder.Build();
        var logger = services.Resolve<ILogger<TestConsoleLoggerTests>>();
        var store = services.Resolve<ILogEventMemoryStore>();

        // Act
        logger.LogInformation("info");
        logger.LogError("error");

        // Assert
        var errorLogs = store.GetLogEvents()[LogEventLevel.Error];
        Assert.That(errorLogs.Count, Is.EqualTo(1));
        var found = errorLogs.Any(e => e.RenderMessage().Contains("error"));
        Assert.That(found, Is.True);
    }
}