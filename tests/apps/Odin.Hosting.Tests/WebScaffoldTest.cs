using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Odin.Hosting.Tests;

public class WebScaffoldTest
{
    protected WebScaffold Scaffold = null!;
    protected IServiceProvider Services => Scaffold.Services;

    [SetUp]
    public void Init()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        Scaffold = new WebScaffold(folder);
        Scaffold.RunBeforeAnyTests();
    }

    //

    [TearDown]
    public void Cleanup()
    {
        Scaffold.RunAfterAnyTests(logEvents =>
        {
            Assert.That(logEvents[Serilog.Events.LogEventLevel.Error].Count, Is.EqualTo(1), "Unexpected number of Error log events");
        });
    }

    //

    [Test]
    public void T01_RunAfterAnyTestsShouldOverrideDefaultLogEventAsserts()
    {
        var logger = Services.GetRequiredService<ILogger<WebScaffoldTest>>();
        logger.LogError("This must be 'caught' in [TearDown] above");
        Assert.Pass();
    }

    [Test]
    public void T02_IndividualTestShouldOverrideDefaultLogEventAsserts()
    {
        Scaffold.SetAssertLogEventsAction(logEvents =>
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
    public void T03_IndividualTestOverrideDefaultLogEventAssertsShouldBeTemporary()
    {
        var logger = Services.GetRequiredService<ILogger<WebScaffoldTest>>();
        logger.LogError("This must be 'caught' in [TearDown] above");
        Assert.Pass();
    }


}