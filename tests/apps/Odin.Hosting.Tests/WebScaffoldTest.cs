using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Test.Helpers.Logging;

namespace Odin.Hosting.Tests;

public class WebScaffoldTest
{
    private WebScaffold _scaffold = null!;
    private IServiceProvider Services => _scaffold.Services;

    [SetUp]
    public void Init()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { });
    }

    //

    [TearDown]
    public void Cleanup()
    {
        if (TestContext.CurrentContext.Test.Name.StartsWith("TearDown"))
        {
            _scaffold.RunAfterAnyTests(logEvents =>
            {
                Assert.That(logEvents[Serilog.Events.LogEventLevel.Error].Count, Is.EqualTo(1), "Unexpected number of Error log events");
            });
        }
        else
        {
            _scaffold.RunAfterAnyTests();
        }
    }

    //

    [Test]
    public void TearDown01_RunAfterAnyTestsShouldOverrideDefaultLogEventAsserts()
    {
        var logger = Services.GetRequiredService<ILogger<WebScaffoldTest>>();
        logger.LogError("This must be 'caught' in [TearDown] above");
        Assert.Pass();
    }

    [Test]
    public void TearDown02_IndividualTestShouldOverrideDefaultLogEventAsserts()
    {
        _scaffold.SetAssertLogEventsAction(logEvents =>
        {
            Assert.That(logEvents[Serilog.Events.LogEventLevel.Error].Count, Is.EqualTo(2), "Unexpected number of Error log events");
            Assert.That(logEvents[Serilog.Events.LogEventLevel.Fatal].Count, Is.EqualTo(2), "Unexpected number of Fatal log events");
        });
        var logger = Services.GetRequiredService<ILogger<WebScaffoldTest>>();
        logger.LogError("This error must be 'caught' in the custom handler above");
        logger.LogError("This error must also be 'caught' in the custom handler above");
        logger.LogCritical("This fatal error must be 'caught' in the custom handler above");
        logger.LogCritical("This fatal error must also be 'caught' in the custom handler above");
        Assert.Pass();
    }

    [Test]
    public void TearDown03_IndividualTestOverrideDefaultLogEventAssertsShouldBeTemporary()
    {
        var logger = Services.GetRequiredService<ILogger<WebScaffoldTest>>();
        logger.LogError("This must be 'caught' in [TearDown] above");
        Assert.Pass();
    }

    [Test]
    public void ItShouldDoOnDemandAssertionOfLogEvents()
    {
        var logger = Services.GetRequiredService<ILogger<WebScaffoldTest>>();
        logger.LogDebug("This must be 'caught' in the AssertLogMessageExists() statement below");

        var logEvents = Services.GetRequiredService<ILogEventMemoryStore>().GetLogEvents();

        LogEvents.AssertLogMessageExists(
            logEvents[Serilog.Events.LogEventLevel.Debug],
            "This must be 'caught' in the AssertLogMessageExists() statement below");
    }

}