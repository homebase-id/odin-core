using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.NeueccMessagePack;

namespace Odin.Core.Storage.Tests.Cache;

#if RUN_REDIS_TESTS

#nullable enable

public class FusionCacheTests
{
    private RedisContainer? _redisContainer;
    private ILifetimeScope? _services;

    [OneTimeSetUp]
    public async Task SetUp()
    {
        _redisContainer = new RedisBuilder()
            .WithImage("redis:latest")
            .Build();
        await _redisContainer.StartAsync();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        if (_redisContainer != null)
        {
            await _redisContainer.StopAsync();
            await _redisContainer.DisposeAsync();
        }
    }

    [Test, Explicit]
    public async Task ItShouldSetAndGetLevel1()
    {
        _services = new ServiceCollection()
            .AddFusionServices()
            .AddFusionLevel1Cache()
            .BuildFusionContainer();

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

    [Test, Explicit]
    public async Task ItShouldSetAndGet2Level1Caches()
    {
        _services = new ServiceCollection()
            .AddFusionServices()
            .AddFusionLevel1Cache("cache1")
            .AddFusionLevel1Cache("cache2")
            .BuildFusionContainer();

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

    [Test, Explicit]
    public async Task ItShouldSetAndGet2Level2Caches()
    {
        _services = new ServiceCollection()
            .AddFusionServices()
            .AddFusionLevel1And2Cache(_redisContainer!.GetConnectionString(), "cache1")
            .AddFusionLevel1And2Cache(_redisContainer!.GetConnectionString(), "cache2")
            .BuildFusionContainer();

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

#region TestServices

public static class FusionCacheTestServices
{
    public static IServiceCollection AddFusionServices(this IServiceCollection services)
    {
        services.AddLogging();
        return services;
    }

    public static IServiceCollection AddFusionLevel1Cache(
        this IServiceCollection services,
        string cacheName = FusionCacheOptions.DefaultCacheName)
    {
        services.AddFusionCache(cacheName)
            .WithOptions(options =>
            {
                options.FailSafeActivationLogLevel = LogLevel.Debug;
                options.SerializationErrorsLogLevel = LogLevel.Warning;
                options.DistributedCacheSyntheticTimeoutsLogLevel = LogLevel.Debug;
                options.DistributedCacheErrorsLogLevel = LogLevel.Error;
                options.FactorySyntheticTimeoutsLogLevel = LogLevel.Debug;
                options.FactoryErrorsLogLevel = LogLevel.Error;
            })
            .WithDefaultEntryOptions(new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromSeconds(30),
            });
        return services;
    }

    public static IServiceCollection AddFusionLevel1And2Cache(
        this IServiceCollection services,
        string connectionString,
        string cacheName = FusionCacheOptions.DefaultCacheName)
    {

        services.AddFusionCache(cacheName)
            .WithOptions(options =>
            {
                options.DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(2);

                // CUSTOM LOG LEVELS
                options.FailSafeActivationLogLevel = LogLevel.Debug;
                options.SerializationErrorsLogLevel = LogLevel.Warning;
                options.DistributedCacheSyntheticTimeoutsLogLevel = LogLevel.Debug;
                options.DistributedCacheErrorsLogLevel = LogLevel.Error;
                options.FactorySyntheticTimeoutsLogLevel = LogLevel.Debug;
                options.FactoryErrorsLogLevel = LogLevel.Error;
            })
            .WithDefaultEntryOptions(new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromSeconds(30),

                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromHours(2),
                FailSafeThrottleDuration = TimeSpan.FromSeconds(30),

                FactorySoftTimeout = TimeSpan.FromMilliseconds(100),
                FactoryHardTimeout = TimeSpan.FromMilliseconds(1500),

                DistributedCacheSoftTimeout = TimeSpan.FromSeconds(1),
                DistributedCacheHardTimeout = TimeSpan.FromSeconds(2),
                AllowBackgroundDistributedCacheOperations = true,

                JitterMaxDuration = TimeSpan.FromSeconds(2)
            })
            .WithSerializer(
                new FusionCacheNeueccMessagePackSerializer()
            )
            .WithDistributedCache(
                new RedisCache(new RedisCacheOptions { Configuration = connectionString })
            )
            .WithCacheKeyPrefix();

        return services;
    }

    public static ILifetimeScope BuildFusionContainer(this IServiceCollection services)
    {
        var builder = new ContainerBuilder();
        builder.Populate(services);
        return builder.Build();
    }
}

#endregion

#endif