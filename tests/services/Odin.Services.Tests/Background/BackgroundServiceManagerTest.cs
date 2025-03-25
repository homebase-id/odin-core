using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.Hostname;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Services.Background;
using Odin.Services.Background.Services;
using Odin.Test.Helpers.Logging;
using Serilog.Events;

namespace Odin.Services.Tests.Background;

public class BackgroundServiceManagerTest
{
    private readonly Odin.Services.Tenant.Tenant _tenant = new ("frodo.hobbit");

    private ILifetimeScope _container = null!;
    private LogEventMemoryStore _logEventMemoryStore = null!;
    private ILogger _logger = null!;

    [SetUp]
    public void Setup()
    {
        _logEventMemoryStore = new LogEventMemoryStore();
        _logger = TestLogFactory.CreateConsoleLogger<BackgroundServiceManager>(_logEventMemoryStore);
        
        var builder = new ContainerBuilder();
        
        builder.RegisterType<CorrelationUniqueIdGenerator>().As<ICorrelationIdGenerator>().SingleInstance();
        builder.RegisterType<CorrelationContext>().As<ICorrelationContext>().SingleInstance();
        builder.RegisterType<StickyHostnameGenerator>().As<IStickyHostnameGenerator>().SingleInstance();
        builder.RegisterType<StickyHostname>().As<IStickyHostname>().SingleInstance();
        
        builder.RegisterInstance(_logger).As<ILogger<BackgroundServiceManager>>();
        builder.RegisterInstance(TestLogFactory.CreateConsoleLogger(_logEventMemoryStore)).As<ILogger>();

        builder.RegisterType<BackgroundServiceManager>()
            .WithParameter(new TypedParameter(typeof(string), _tenant.ToString()))
            .As<IBackgroundServiceManager>()
            .SingleInstance();

        builder.RegisterType<ThrowingBackgroundService>().InstancePerDependency();
        builder.RegisterType<NoOpBackgroundService>().InstancePerDependency();
        builder.RegisterType<LoopingBackgroundService>().InstancePerDependency();
        builder.RegisterType<LoopingBackgroundServiceWithSleepAndWakeUp>().InstancePerDependency();
        builder.RegisterType<ResetEventDemo>().InstancePerDependency();
        builder.RegisterType<BackgroundServiceWithBadSleep>().InstancePerDependency();

        builder.RegisterType<ScopedTestValue>().InstancePerLifetimeScope();
        builder.RegisterType<ScopeTestBackgroundService>().InstancePerDependency();

        builder.RegisterType<PulseTestBackgroundService>().InstancePerDependency();
        builder.RegisterType<BackgroundServiceTrigger<PulseTestBackgroundService>>()
            .As<IBackgroundServiceTrigger<PulseTestBackgroundService>>()
            .SingleInstance();

        _container = builder.Build();
    }

    private void AssertLogEvents()
    {
        LogEvents.AssertEvents(_logEventMemoryStore.GetLogEvents());
    }

    [Test]
    public async Task ItShouldLogUnhandledExceptions()
    {
        var manager = _container.Resolve<IBackgroundServiceManager>();
        await manager.StartAsync<ThrowingBackgroundService>("dummy-service");
        await Task.Delay(1);

        var logEvents = _logEventMemoryStore.GetLogEvents();
        Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(1));
        LogEvents.AssertLogMessageExists(logEvents[LogEventLevel.Error],
            "BackgroundService \"ThrowingBackgroundService\" is exiting because of an unhandled exception: \"crash and burn!\"");
    }

    [Test]
    public void ItShouldThrowOnUnknownService()
    {
        var manager = _container.Resolve<IBackgroundServiceManager>();
        
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => 
            await manager.StartAsync(_container.Resolve<NoOpBackgroundService>())); 
        
        ClassicAssert.AreEqual("Background service not found. Did you forget to call Create?", exception?.Message);
    }

    [Test]
    public void ItShouldThrowOnDuplicateServiceIdentifier()
    {
        var manager = _container.Resolve<IBackgroundServiceManager>();

        manager.Create<NoOpBackgroundService>("asdasdasd");
        
        var exception = Assert.Throws<InvalidOperationException>(() => 
             manager.Create<NoOpBackgroundService>("asdasdasd")); 
        
        ClassicAssert.AreEqual("Background service 'asdasdasd' already exists.", exception?.Message);
    }
    
    [Test]
    public async Task ItShouldStartAndStopAServiceWithoutLoop()
    {
        var manager = _container.Resolve<IBackgroundServiceManager>();

        var service = await manager.StartAsync<NoOpBackgroundService>("dummy-service");
        await Task.Delay(1);

        ClassicAssert.True(service.DidInitialize);
        ClassicAssert.True(service.DidFinish);
        ClassicAssert.False(service.DidShutdown);
        ClassicAssert.False(service.DidDispose);

        await manager.StopAsync("dummy-service");
        ClassicAssert.True(service.DidInitialize);
        ClassicAssert.True(service.DidFinish);
        ClassicAssert.True(service.DidShutdown);
        ClassicAssert.True(service.DidDispose);

        AssertLogEvents();
    }

    [Test]
    public async Task ItShouldStartAndStopAServiceWithLoop()
    {
        var manager = _container.Resolve<IBackgroundServiceManager>();

        var sw = Stopwatch.StartNew();

        var service = await manager.StartAsync<LoopingBackgroundService>("dummy-service");
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));

        await Task.Delay(1);
        ClassicAssert.True(service.DidInitialize);
        ClassicAssert.False(service.DidFinish);
        ClassicAssert.False(service.DidShutdown);
        ClassicAssert.False(service.DidDispose);

        await manager.StopAsync("dummy-service");
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        ClassicAssert.True(service.DidInitialize);
        ClassicAssert.True(service.DidFinish);
        ClassicAssert.True(service.DidShutdown);
        ClassicAssert.True(service.DidDispose);

        AssertLogEvents();
    }

     [Test]
     public async Task ItShouldStartAndStopManyServicesWithLoop()
     {
         var manager = _container.Resolve<IBackgroundServiceManager>();
         
         const int serviceCount = 100;
         var services = new List<Task<LoopingBackgroundService>>();
         for (var i = 0; i < serviceCount; i++)
         {
             var service = manager.StartAsync<LoopingBackgroundService>(Guid.NewGuid().ToString());
             services.Add(service);
         }

         var sw = Stopwatch.StartNew();

         await Task.WhenAll(services);

         Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));

         await Task.Delay(1);
         foreach (var service in services.Select(s => s.Result))
         {
             ClassicAssert.True(service.DidInitialize);
             ClassicAssert.False(service.DidFinish);
             ClassicAssert.False(service.DidShutdown);
             ClassicAssert.False(service.DidDispose);
         }

         await manager.ShutdownAsync();
         Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
         foreach (var service in services.Select(s => s.Result))
         {
             ClassicAssert.True(service.DidInitialize);
             ClassicAssert.True(service.DidFinish);
             ClassicAssert.True(service.DidShutdown);
             ClassicAssert.True(service.DidDispose);
         }

         AssertLogEvents();
     }

#if !CI_GITHUB
    [Test]
    public async Task ItShouldStartAndStopManyServicesWithLoopSleepAndWakeUpManyTimes()
    {
        var manager = _container.Resolve<IBackgroundServiceManager>();

        for (var iteration = 0; iteration < 3; iteration++)
        {
            const int serviceCount = 100;
            
            var services = new List<Task<LoopingBackgroundServiceWithSleepAndWakeUp>>();
            for (var i = 0; i < serviceCount; i++)
            {
                var service = manager.StartAsync<LoopingBackgroundServiceWithSleepAndWakeUp>($"instance{i*(iteration+1)}");
                services.Add(service);
            }

            var sw = Stopwatch.StartNew();

            await Task.WhenAll(services);

            // PulseBackgroundProcessor 3 times
            for (var idx = 0; idx < 3; idx++)
            {
                for (var i = 0; i < serviceCount; i++)
                {
                    manager.PulseBackgroundProcessor($"instance{i*(iteration+1)}");
                }
                await Task.Delay(200);
            }

            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            foreach (var service in services.Select(s => s.Result))
            {
                ClassicAssert.True(service.DidInitialize);
                ClassicAssert.False(service.DidFinish);
                ClassicAssert.False(service.DidShutdown);
                ClassicAssert.False(service.DidDispose);
                ClassicAssert.AreEqual(3, service.Counter);
            }

            await manager.StopAllAsync();
            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            foreach (var service in services.Select(s => s.Result))
            {
                ClassicAssert.True(service.DidInitialize);
                ClassicAssert.True(service.DidFinish);
                ClassicAssert.True(service.DidShutdown);
                ClassicAssert.True(service.DidDispose);
                ClassicAssert.AreEqual(3, service.Counter);
            }
        }

        await manager.ShutdownAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await manager.StartAsync<LoopingBackgroundServiceWithSleepAndWakeUp>(Guid.NewGuid().ToString());
        });
        ClassicAssert.AreEqual("The background service manager is stopping.", exception?.Message);

        AssertLogEvents();
    }
#endif

#if !CI_GITHUB    
    [Test]
    public async Task WillFailIfBackgroundServiceManagerUsesAutoResetEventInsteadOfManualResetEvent()
    {
        var manager = _container.Resolve<IBackgroundServiceManager>();
        var service = await manager.StartAsync<ResetEventDemo>("foo");

        var sw = Stopwatch.StartNew();
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
        
        await Task.Delay(200);
        ClassicAssert.AreEqual(1, service.Counter);
        
        manager.PulseBackgroundProcessor("foo");
        await Task.Delay(200);
        
        ClassicAssert.AreEqual(2, service.Counter);

        AssertLogEvents();
    }
#endif

     [Test]
     public async Task ItShouldResetDurationOnBadSleepDuration()
     {
         var manager = _container.Resolve<IBackgroundServiceManager>();

         // Good sleep
         {
             var sleep = TimeSpan.Zero;

             var service = manager.Create<BackgroundServiceWithBadSleep>(Guid.NewGuid().ToString());
             service.Duration1 = sleep;
             service.Duration2 = sleep;
             var sw = Stopwatch.StartNew();
         
             await manager.StartAsync(service);
             Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
             
             AssertLogEvents();
         }
         
         // Good sleep
         {
             var sleep = TimeSpan.FromMilliseconds(1);
 
             var service = manager.Create<BackgroundServiceWithBadSleep>(Guid.NewGuid().ToString());
             service.Duration1 = sleep;
             service.Duration2 = sleep;
             var sw = Stopwatch.StartNew();
         
             await manager.StartAsync(service);
             Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
             
             AssertLogEvents();
         }
         
         // Bad sleep
         {
             _logEventMemoryStore.Clear();
             
             var sleep = TimeSpan.FromMilliseconds(-1); // -1 means "infinite" when calling Task.Delay

             var service = manager.Create<BackgroundServiceWithBadSleep>(Guid.NewGuid().ToString());
             service.Duration1 = sleep;
             service.Duration2 = sleep;
         
             await manager.StartAsync(service);
 
             var logEvents = _logEventMemoryStore.GetLogEvents();
             LogEvents.AssertLogMessageExists(logEvents[LogEventLevel.Debug], $"Invalid duration1 {sleep.TotalMilliseconds}ms. Resetting to min.");
         
             AssertLogEvents();
         }
         
         // Bad sleep
         {
             _logEventMemoryStore.Clear();
             
             var sleep = AbstractBackgroundService.MaxSleepDuration.Add(TimeSpan.FromMilliseconds(1));

             var service = manager.Create<BackgroundServiceWithBadSleep>(Guid.NewGuid().ToString());
             service.Duration1 = sleep;
             service.Duration2 = sleep;
             await manager.StartAsync(service);

             var logEvents = _logEventMemoryStore.GetLogEvents();
             LogEvents.AssertLogMessageExists(logEvents[LogEventLevel.Debug], $"Invalid duration1 {sleep.TotalMilliseconds}ms. Resetting to max.");
             
             AssertLogEvents();
         }
         
         // Bad sleep
         {
             var sleep1 = TimeSpan.FromMilliseconds(2);
             var sleep2 = TimeSpan.FromMilliseconds(1);

             var service = manager.Create<BackgroundServiceWithBadSleep>(Guid.NewGuid().ToString());
             service.Duration1 = sleep1;
             service.Duration2 = sleep2;
             await manager.StartAsync(service);

             var logEvents = _logEventMemoryStore.GetLogEvents();
             Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(1));
         
             var error = logEvents[LogEventLevel.Error][0].RenderMessage();
             Assert.That(error, Is.EqualTo($"BackgroundService \"BackgroundServiceWithBadSleep\" is exiting because of an unhandled exception: \"duration1 must be less than or equal to duration2\""));
         }
     }
     
     [Test]
     public async Task ItShouldCreateChildScopeInServices()
     {
         //
         // NOTE:
         //
         // To see this test fail because of wrong scope:
         // - go to BackgroundServiceManager::Create<T>
         // - change the line
         //     var backgroundService = serviceScope.Resolve<T>();
         //   to
         //     var backgroundService = lifetimeScope.Resolve<T>();
         //   this will cause the test to fail because the ScopedTestValue will be resolved from the parent scope
         //   and not the child scope.
         //
         
         var manager = _container.Resolve<IBackgroundServiceManager>();
         
         // Sanity
         var scopedTestValue = _container.Resolve<ScopedTestValue>();
         scopedTestValue.Value = "sanity";
         scopedTestValue = _container.Resolve<ScopedTestValue>();
         Assert.That(scopedTestValue.Value, Is.EqualTo("sanity"));

         var service =  manager.Create<ScopeTestBackgroundService>("scope-test");

         ClassicAssert.False(service.DidInitialize);
         ClassicAssert.False(service.DidFinish);
         ClassicAssert.False(service.DidShutdown);
         ClassicAssert.False(service.DidDispose);
         ClassicAssert.AreEqual("new born", service.ScopedTestValue.Value);
         
         await manager.StartAsync(service);
         await Task.Delay(1);

         ClassicAssert.True(service.DidInitialize);
         ClassicAssert.True(service.DidFinish);
         ClassicAssert.False(service.DidShutdown);
         ClassicAssert.False(service.DidDispose);
         ClassicAssert.AreEqual("ExecuteAsync", service.ScopedTestValue.Value);
        
         await manager.StopAsync("scope-test");
         ClassicAssert.True(service.DidInitialize);
         ClassicAssert.True(service.DidFinish);
         ClassicAssert.True(service.DidShutdown);
         ClassicAssert.True(service.DidDispose);
         ClassicAssert.AreEqual("StoppedAsync", service.ScopedTestValue.Value);
         
         scopedTestValue = _container.Resolve<ScopedTestValue>();
         Assert.That(scopedTestValue.Value, Is.EqualTo("sanity"));

         AssertLogEvents();
     }

     [Test]
     public async Task ItShouldWakeSleepingBackgroundService()
     {
         var manager = _container.Resolve<IBackgroundServiceManager>();

         var service = await manager.StartAsync<PulseTestBackgroundService>();
         await Task.Delay(1);

         ClassicAssert.True(service.DidInitialize);
         ClassicAssert.False(service.DidFinish);
         ClassicAssert.False(service.DidShutdown);
         ClassicAssert.False(service.DidDispose);

         ClassicAssert.False(service.Pulsed);

         var otherService = _container.Resolve<PulseTestBackgroundService>();
         var trigger = _container.Resolve<IBackgroundServiceTrigger<PulseTestBackgroundService>>();
         trigger.PulseBackgroundProcessor();
         await Task.Delay(10);
         ClassicAssert.True(service.Pulsed);
         ClassicAssert.False(otherService.Pulsed);

         await manager.StopAsync<PulseTestBackgroundService>();
         ClassicAssert.True(service.DidInitialize);
         ClassicAssert.True(service.DidFinish);
         ClassicAssert.True(service.DidShutdown);
         ClassicAssert.True(service.DidDispose);

         AssertLogEvents();
     }

}

public abstract class BaseBackgroundService(ILogger logger)
    : AbstractBackgroundService(logger), IDisposable
{
    public volatile bool DidInitialize;
    public volatile bool DidFinish;
    public volatile bool DidShutdown;
    public volatile bool DidDispose;

    public void Dispose()
    {
        DidDispose = true;
        GC.SuppressFinalize(this);
    }

    protected override Task StartingAsync(CancellationToken stoppingToken)
    {
        DidInitialize = true;
        return Task.CompletedTask;
    }

    protected override Task StoppedAsync(CancellationToken stoppingToken)
    {
        DidShutdown = true;
        return Task.CompletedTask;
    }
}

public class ThrowingBackgroundService(ILogger logger) : BaseBackgroundService(logger)
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        throw new Exception("crash and burn!");
    }
}

public class NoOpBackgroundService(ILogger logger) : BaseBackgroundService(logger)
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DidFinish = true;
        return Task.CompletedTask;
    }
}

public class LoopingBackgroundService(ILogger logger) : BaseBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        finally
        {
            DidFinish = true;
        }
    }
}

public class LoopingBackgroundServiceWithSleepAndWakeUp(ILogger logger)
    : BaseBackgroundService(logger)
{
    public int Counter  { get; private set; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await SleepAsync(TimeSpan.FromSeconds(10), stoppingToken);
                if (!stoppingToken.IsCancellationRequested)
                {
                    // Simulate some work
                    Counter++;
                }
            }
        }
        finally
        {
            DidFinish = true;
        }
    }
}

public class ResetEventDemo(ILogger logger) : BaseBackgroundService(logger)
{
    private int _counter;
    public int Counter
    {
        get => Interlocked.Exchange(ref _counter, _counter);
        // private set => Interlocked.Exchange(ref _counter, value);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SleepAsync(TimeSpan.FromMilliseconds(100), stoppingToken);
            Interlocked.Increment(ref _counter);
            await SleepAsync(TimeSpan.FromMilliseconds(2000), stoppingToken);
            Interlocked.Increment(ref _counter);
        }
        finally
        {
            DidFinish = true;
        }
    }
}

public class BackgroundServiceWithBadSleep(ILogger logger) : BaseBackgroundService(logger)
{
    public TimeSpan Duration1 { get; set; }
    public TimeSpan Duration2 { get; set; }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SleepAsync(Duration1, Duration2, stoppingToken);
    }
}

public class ScopedTestValue
{
    public string Value { get; set; } = "new born";
}

public class ScopeTestBackgroundService(ILogger logger, ScopedTestValue scopedTestValue) : BaseBackgroundService(logger)
{
    public ScopedTestValue ScopedTestValue { get; } = scopedTestValue;

    protected override Task StartingAsync(CancellationToken stoppingToken)
    {
        ScopedTestValue.Value = "StartingAsync";
        return base.StartingAsync(stoppingToken);
    }
    
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ScopedTestValue.Value = "ExecuteAsync";
        DidFinish = true;
        return Task.CompletedTask;
    }
    
    protected override Task StoppedAsync(CancellationToken stoppingToken)
    {
        ScopedTestValue.Value = "StoppedAsync";
        return base.StoppedAsync(stoppingToken);
    }
}

public class PulseTestBackgroundService(ILogger logger) : BaseBackgroundService(logger)
{
    private volatile bool _pulsed;
    public bool Pulsed => _pulsed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SleepAsync(TimeSpan.FromHours(1), stoppingToken);
            if (!stoppingToken.IsCancellationRequested)
            {
                _pulsed = true;
            }
        }
        DidFinish = true;
    }
}
