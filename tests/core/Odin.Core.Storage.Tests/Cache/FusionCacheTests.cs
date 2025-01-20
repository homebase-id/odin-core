using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace Odin.Core.Storage.Tests.Cache;

#if RUN_REDIS_TESTS

#nullable enable

public class FusionCacheTests
{
    private RedisContainer? _redisContainer;
    private ILifetimeScope? _services;

    [SetUp]
    public void Setup()
    {
        _redisContainer = new RedisBuilder()
            .WithImage("redis:latest")
            .Build();
        _redisContainer.StartAsync().Wait();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddFusionCache()
            .WithDefaultEntryOptions(new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromSeconds(2)
            });

        serviceCollection.AddFusionCache("cache1");
        serviceCollection.AddFusionCache("cache2");

        var builder = new ContainerBuilder();
        builder.Populate(serviceCollection);

        _services = builder.Build();
    }

    [TearDown]
    public void TearDown()
    {
        _redisContainer?.StopAsync().Wait();
        _redisContainer?.DisposeAsync().AsTask().Wait();
    }

    [Test]
    public async Task ItShouldSetAndGetLevel1()
    {
        var cache = _services!.Resolve<IFusionCache>();

        var id = Guid.NewGuid();

        var record1 = await cache.GetOrSetAsync<PocoA?>(
            $"poco:{id}",
            _ => GetProductFromDbAsync(id),
            TimeSpan.FromSeconds(30)
        );

        var record2 = cache.GetOrSet<PocoA?>(
            $"poco:{id}",
            _ => GetProductFromDbAsync(id).Result,
            TimeSpan.FromSeconds(30)
        );

        Assert.That(record1!.Id, Is.EqualTo(record2!.Id));
        Assert.That(record1.Uuid, Is.Not.EqualTo(Guid.Empty));
        Assert.That(record1.Uuid, Is.EqualTo(record2.Uuid));
    }

    //

    [Test]
    public async Task ItShouldSetAndGet2Level1Caches()
    {
        var cacheProvider = _services!.Resolve<IFusionCacheProvider>();

        var cache1 = cacheProvider.GetCache("cache1");
        var cache2 = cacheProvider.GetCache("cache2");

        var id = Guid.NewGuid();

        var record1 = await cache1.GetOrSetAsync<PocoA?>(
            $"poco:{id}",
            _ => GetProductFromDbAsync(id),
            TimeSpan.FromSeconds(30)
        );

        var record2 = await cache2.GetOrSetAsync<PocoA?>(
            $"poco:{id}",
            _ => GetProductFromDbAsync(id),
            TimeSpan.FromSeconds(30)
        );

        Assert.That(record1!.Id, Is.EqualTo(record2!.Id));
        Assert.That(record1.Uuid, Is.Not.EqualTo(Guid.Empty));
        Assert.That(record1.Uuid, Is.Not.EqualTo(record2.Uuid));
    }


    //

    private Task<PocoA?> GetProductFromDbAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            return Task.FromResult<PocoA?>(null);
        }

        var record = new PocoA
        {
            Id = id,
            Uuid = Guid.NewGuid(),
        };

        return Task.FromResult(record)!;
    }

    public class PocoA
    {
        public Guid Id { get; set; }
        public Guid Uuid { get; set; }
    }


}

#endif