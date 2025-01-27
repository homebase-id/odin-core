using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Storage.Cache;
using Testcontainers.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Serialization.NeueccMessagePack;

namespace Odin.Core.Storage.Tests.Cache;

#nullable enable

public class OdinCacheTests
{
    private RedisContainer? _redisContainer;
    private ILifetimeScope? _services;

    [SetUp]
    public void Setup()
    {
    }

    //

    [TearDown]
    public async Task TearDown()
    {
        if (_redisContainer != null)
        {
            await _redisContainer.StopAsync();
            await _redisContainer.DisposeAsync();
            _redisContainer = null;
        }
        _services?.Dispose();
        _services = null;
    }

    //

    private async Task RegisterServicesAsync(Level2CacheType level2CacheType)
    {
        if (level2CacheType == Level2CacheType.Redis)
        {
            _redisContainer = new RedisBuilder()
                .WithImage("redis:latest")
                .Build();
            await _redisContainer.StartAsync();
        }

        var services = new ServiceCollection();

        services.AddLogging();

        services.AddSingleton(new OdinCacheKeyPrefix("some-prefix"));
        services.AddSingleton<IOdinCache, OdinCache>();

        var fusionBuilder = services.AddFusionCache()
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
            );

        if (level2CacheType == Level2CacheType.Redis)
        {
            fusionBuilder.WithDistributedCache(
                new RedisCache(new RedisCacheOptions { Configuration = _redisContainer!.GetConnectionString() }));
        }

        var builder = new ContainerBuilder();
        builder.Populate(services);
        _services = builder.Build();
    }

    [Test]
    [TestCase(Level2CacheType.None)]
#if RUN_REDIS_TESTS
    [TestCase(Level2CacheType.Redis)]
#endif
    public async Task ItShouldGetAndSet(Level2CacheType level2CacheType)
    {
        await RegisterServicesAsync(level2CacheType);

        var cache = _services!.Resolve<IOdinCache>();

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

    #region POCO

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

    #endregion

}

