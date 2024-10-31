using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
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
    private readonly Services.Tenant.Tenant _tenant = new ("frodo.hobbit");

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
        
        builder.RegisterType<ThrowingBackgroundService>().InstancePerDependency();
        builder.RegisterType<NoOpBackgroundService>().InstancePerDependency();
        builder.RegisterType<LoopingBackgroundService>().InstancePerDependency();
        builder.RegisterType<LoopingBackgroundServiceWithSleepAndWakeUp>().InstancePerDependency();
        builder.RegisterType<ResetEventDemo>().InstancePerDependency();
        builder.RegisterType<BackgroundServiceWithBadSleep>().InstancePerDependency();

        builder.RegisterType<ScopedTestValue>().InstancePerLifetimeScope();
        builder.RegisterType<ScopeTestBackgroundService>().InstancePerDependency();
        
        _container = builder.Build();
    }

    private void AssertLogEvents()
    {
        LogEvents.AssertEvents(_logEventMemoryStore.GetLogEvents());
    }

    [Test]
    public async Task ItShouldLogUnhandledExceptions()
    {
        var manager = new BackgroundServiceManager(_container, _tenant.Name);
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
        var manager = new BackgroundServiceManager(_container, _tenant.Name);
        
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => 
            await manager.StartAsync(_container.Resolve<NoOpBackgroundService>())); 
        
        Assert.AreEqual("Background service not found. Did you forget to call Create?", exception?.Message);
    }

    [Test]
    public void ItShouldThrowOnDuplicateServiceIdentifier()
    {
        var manager = new BackgroundServiceManager(_container, _tenant.Name);

        manager.Create<NoOpBackgroundService>("asdasdasd");
        
        var exception = Assert.Throws<InvalidOperationException>(() => 
             manager.Create<NoOpBackgroundService>("asdasdasd")); 
        
        Assert.AreEqual("Background service 'asdasdasd' already exists.", exception?.Message);
    }
    
    [Test]
    public async Task ItShouldStartAndStopAServiceWithoutLoop()
    {
        var manager = new BackgroundServiceManager(_container, _tenant.Name);

        var service = await manager.StartAsync<NoOpBackgroundService>("dummy-service");
        await Task.Delay(1);

        Assert.True(service.DidInitialize);
        Assert.True(service.DidFinish);
        Assert.False(service.DidShutdown);
        Assert.False(service.DidDispose);

        await manager.StopAsync("dummy-service");
        Assert.True(service.DidInitialize);
        Assert.True(service.DidFinish);
        Assert.True(service.DidShutdown);
        Assert.True(service.DidDispose);

        AssertLogEvents();
    }

    [Test]
    public async Task ItShouldStartAndStopAServiceWithLoop()
    {
        var manager = new BackgroundServiceManager(_container, _tenant.Name);

        var sw = Stopwatch.StartNew();

        var service = await manager.StartAsync<LoopingBackgroundService>("dummy-service");
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));

        await Task.Delay(1);
        Assert.True(service.DidInitialize);
        Assert.False(service.DidFinish);
        Assert.False(service.DidShutdown);
        Assert.False(service.DidDispose);

        await manager.StopAsync("dummy-service");
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        Assert.True(service.DidInitialize);
        Assert.True(service.DidFinish);
        Assert.True(service.DidShutdown);
        Assert.True(service.DidDispose);

        AssertLogEvents();
    }

     [Test]
     public async Task ItShouldStartAndStopManyServicesWithLoop()
     {
         var manager = new BackgroundServiceManager(_container, _tenant.Name);
         
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
             Assert.True(service.DidInitialize);
             Assert.False(service.DidFinish);
             Assert.False(service.DidShutdown);
             Assert.False(service.DidDispose);
         }

         await manager.ShutdownAsync();
         Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
         foreach (var service in services.Select(s => s.Result))
         {
             Assert.True(service.DidInitialize);
             Assert.True(service.DidFinish);
             Assert.True(service.DidShutdown);
             Assert.True(service.DidDispose);
         }

         AssertLogEvents();
     }

#if !NOISY_NEIGHBOUR
    [Test]
    public async Task ItShouldStartAndStopManyServicesWithLoopSleepAndWakeUpManyTimes()
    {
        var manager = new BackgroundServiceManager(_container, _tenant.Name);
        
        for (var iteration = 0; iteration < 3; iteration++)
        {
            const int serviceCount = 100;
            
            var services = new List<Task<LoopingBackgroundServiceWithSleepAndWakeUp>>();
            for (var i = 0; i < serviceCount; i++)
            {
                var service = manager.StartAsync<LoopingBackgroundServiceWithSleepAndWakeUp>(Guid.NewGuid().ToString());
                services.Add(service);
            }

            var sw = Stopwatch.StartNew();

            await Task.WhenAll(services);

            // PulseBackgroundProcessor 3 times
            for (var idx = 0; idx < 3; idx++)
            {
                foreach (var service in services.Select(s => s.Result))
                {
                    service.PulseBackgroundProcessor();
                }
                await Task.Delay(200);
            }

            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            foreach (var service in services.Select(s => s.Result))
            {
                Assert.True(service.DidInitialize);
                Assert.False(service.DidFinish);
                Assert.False(service.DidShutdown);
                Assert.False(service.DidDispose);
                Assert.AreEqual(3, service.Counter);
            }

            await manager.StopAllAsync();
            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            foreach (var service in services.Select(s => s.Result))
            {
                Assert.True(service.DidInitialize);
                Assert.True(service.DidFinish);
                Assert.True(service.DidShutdown);
                Assert.True(service.DidDispose);
                Assert.AreEqual(3, service.Counter);
            }
        }

        await manager.ShutdownAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await manager.StartAsync<LoopingBackgroundServiceWithSleepAndWakeUp>(Guid.NewGuid().ToString());
        });
        Assert.AreEqual("The background service manager is stopping.", exception?.Message);

        AssertLogEvents();
    }
#endif

#if !NOISY_NEIGHBOUR    
    [Test]
    public async Task WillFailIfBackgroundServiceManagerUsesAutoResetEventInsteadOfManualResetEvent()
    {
        var manager = new BackgroundServiceManager(_container, _tenant.Name);
        var service = await manager.StartAsync<ResetEventDemo>("foo");

        var sw = Stopwatch.StartNew();
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
        
        await Task.Delay(200);
        Assert.AreEqual(1, service.Counter);
        
        service.PulseBackgroundProcessor();
        await Task.Delay(200);
        
        Assert.AreEqual(2, service.Counter);

        AssertLogEvents();
    }
#endif

     [Test]
     public async Task ItShouldResetDurationOnBadSleepDuration()
     {
         var manager = new BackgroundServiceManager(_container, _tenant.Name);

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
         
         var manager = new BackgroundServiceManager(_container, _tenant.Name);
         
         // Sanity
         var scopedTestValue = _container.Resolve<ScopedTestValue>();
         scopedTestValue.Value = "sanity";
         scopedTestValue = _container.Resolve<ScopedTestValue>();
         Assert.That(scopedTestValue.Value, Is.EqualTo("sanity"));

         var service =  manager.Create<ScopeTestBackgroundService>("scope-test");

         Assert.False(service.DidInitialize);
         Assert.False(service.DidFinish);
         Assert.False(service.DidShutdown);
         Assert.False(service.DidDispose);
         Assert.AreEqual("new born", service.ScopedTestValue.Value);
         
         await manager.StartAsync(service);
         await Task.Delay(1);

         Assert.True(service.DidInitialize);
         Assert.True(service.DidFinish);
         Assert.False(service.DidShutdown);
         Assert.False(service.DidDispose);
         Assert.AreEqual("ExecuteAsync", service.ScopedTestValue.Value);
        
         await manager.StopAsync("scope-test");
         Assert.True(service.DidInitialize);
         Assert.True(service.DidFinish);
         Assert.True(service.DidShutdown);
         Assert.True(service.DidDispose);
         Assert.AreEqual("StoppedAsync", service.ScopedTestValue.Value);
         
         scopedTestValue = _container.Resolve<ScopedTestValue>();
         Assert.That(scopedTestValue.Value, Is.EqualTo("sanity"));

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
