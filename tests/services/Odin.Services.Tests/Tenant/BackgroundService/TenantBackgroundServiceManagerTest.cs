using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Tenant.BackgroundService;
using Odin.Test.Helpers.Logging;

namespace Odin.Services.Tests.Tenant.BackgroundService;

public class TenantBackgroundServiceManagerTest
{
    [Test]
    public async Task ItShouldStartAndStopAServiceWithoutLoop()
    {
        var logger = TestLogFactory.CreateConsoleLogger<TenantBackgroundServiceManager>();
        var tenant = new Services.Tenant.Tenant("frodo.hobbit");
        var manager = new TenantBackgroundServiceManager(logger, tenant);

        var service = new NoOpBackgroundService(tenant);
        Assert.False(service.DidInitialize);
        Assert.False(service.DidFinish);
        Assert.False(service.DidShutdown);
        Assert.False(service.DidDispose);

        await manager.StartAsync("dummy-service", service);
        await Task.Delay(1);

        Assert.True(service.DidInitialize);
        Assert.True(service.DidFinish);
        Assert.False(service.DidShutdown);
        Assert.False(service.DidDispose);

        await manager.StopAsync("dummy-service");
        Assert.True(service.DidInitialize);
        Assert.True(service.DidFinish);
        Assert.True(service.DidShutdown);
        Assert.False(service.DidDispose);

        service.Dispose();
        Assert.True(service.DidInitialize);
        Assert.True(service.DidFinish);
        Assert.True(service.DidShutdown);
        Assert.True(service.DidDispose);
    }

    [Test]
    public async Task ItShouldStartAndStopAServiceWithLoop()
    {
        var logger = TestLogFactory.CreateConsoleLogger<TenantBackgroundServiceManager>();
        var tenant = new Services.Tenant.Tenant("frodo.hobbit");
        var manager = new TenantBackgroundServiceManager(logger, tenant);

        var service = new LoopingBackgroundService(tenant);
        Assert.False(service.DidInitialize);
        Assert.False(service.DidFinish);
        Assert.False(service.DidShutdown);
        Assert.False(service.DidDispose);

        var sw = Stopwatch.StartNew();

        await manager.StartAsync("dummy-service", service);
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
        Assert.False(service.DidDispose);

        service.Dispose();
        Assert.True(service.DidInitialize);
        Assert.True(service.DidFinish);
        Assert.True(service.DidShutdown);
        Assert.True(service.DidDispose);
    }

    [Test]
    public async Task ItShouldStartAndStopManyServicesWithLoop()
    {
        var logger = TestLogFactory.CreateConsoleLogger<TenantBackgroundServiceManager>();
        var tenant = new Services.Tenant.Tenant("frodo.hobbit");
        var manager = new TenantBackgroundServiceManager(logger, tenant);

        const int serviceCount = 100;
        var services = new List<LoopingBackgroundService>();
        for (var i = 0; i < serviceCount; i++)
        {
            var service = new LoopingBackgroundService(tenant);
            services.Add(service);
            Assert.False(service.DidInitialize);
            Assert.False(service.DidFinish);
            Assert.False(service.DidShutdown);
            Assert.False(service.DidDispose);
        }

        var sw = Stopwatch.StartNew();

        var tasks = new List<Task>();
        foreach (var service in services)
        {
            tasks.Add(manager.StartAsync(Guid.NewGuid().ToString(), service));
        }
        await Task.WhenAll(tasks);
        tasks.Clear();

        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));

        await Task.Delay(1);
        foreach (var service in services)
        {
            Assert.True(service.DidInitialize);
            Assert.False(service.DidFinish);
            Assert.False(service.DidShutdown);
            Assert.False(service.DidDispose);
        }

        await manager.ShutdownAsync();
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        foreach (var service in services)
        {
            Assert.True(service.DidInitialize);
            Assert.True(service.DidFinish);
            Assert.True(service.DidShutdown);
            Assert.False(service.DidDispose);
        }

        foreach (var service in services)
        {
            service.Dispose();
            Assert.True(service.DidInitialize);
            Assert.True(service.DidFinish);
            Assert.True(service.DidShutdown);
            Assert.True(service.DidDispose);
        }
    }

#if !NOISY_NEIGHBOUR
    [Test]
    public async Task ItShouldStartAndStopManyServicesWithLoopSleepAndWakeUpManyTimes()
    {
        var logger = TestLogFactory.CreateConsoleLogger<TenantBackgroundServiceManager>();
        var tenant = new Services.Tenant.Tenant("frodo.hobbit");
        var manager = new TenantBackgroundServiceManager(logger, tenant);

        for (var iteration = 0; iteration < 3; iteration++)
        {
            const int serviceCount = 100;
            var services = new List<LoopingBackgroundServiceWithSleepAndWakeUp>();
            for (var i = 0; i < serviceCount; i++)
            {
                var service = new LoopingBackgroundServiceWithSleepAndWakeUp(tenant);
                services.Add(service);
                Assert.False(service.DidInitialize);
                Assert.False(service.DidFinish);
                Assert.False(service.DidShutdown);
                Assert.False(service.DidDispose);
                Assert.AreEqual(0, service.Counter);
            }

            var sw = Stopwatch.StartNew();

            var tasks = new List<Task>();
            foreach (var service in services)
            {
                tasks.Add(manager.StartAsync(Guid.NewGuid().ToString(), service));
            }

            await Task.WhenAll(tasks);
            tasks.Clear();

            // WakeUp 3 times
            for (var idx = 0; idx < 3; idx++)
            {
                foreach (var service in services)
                {
                    service.Pulse();
                }
                await Task.Delay(200);
            }

            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            foreach (var service in services)
            {
                Assert.True(service.DidInitialize);
                Assert.False(service.DidFinish);
                Assert.False(service.DidShutdown);
                Assert.False(service.DidDispose);
                Assert.AreEqual(3, service.Counter);
            }

            await manager.StopAllAsync();
            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            foreach (var service in services)
            {
                Assert.True(service.DidInitialize);
                Assert.True(service.DidFinish);
                Assert.True(service.DidShutdown);
                Assert.False(service.DidDispose);
                Assert.AreEqual(3, service.Counter);
            }

            foreach (var service in services)
            {
                service.Dispose();
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
            var service = new LoopingBackgroundServiceWithSleepAndWakeUp(tenant);
            await manager.StartAsync(Guid.NewGuid().ToString(), service);
        });
        Assert.AreEqual("The background service is stopping.", exception?.Message);
    }
#endif

}

public abstract class BaseBackgroundService(Services.Tenant.Tenant tenant) : AbstractTenantBackgroundService(tenant), IDisposable
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

    public override Task StartingAsync(CancellationToken stoppingToken)
    {
        DidInitialize = true;
        return Task.CompletedTask;
    }

    public override Task StoppedAsync(CancellationToken stoppingToken)
    {
        DidShutdown = true;
        return Task.CompletedTask;
    }
}

public class NoOpBackgroundService(Services.Tenant.Tenant tenant) : BaseBackgroundService(tenant)
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DidFinish = true;
        return Task.CompletedTask;
    }
}

public class LoopingBackgroundService(Services.Tenant.Tenant tenant) : BaseBackgroundService(tenant)
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

public class LoopingBackgroundServiceWithSleepAndWakeUp(Services.Tenant.Tenant tenant) : BaseBackgroundService(tenant)
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
