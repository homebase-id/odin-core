using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.NeueccMessagePack;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Odin.Core.Storage.Cache;

#nullable enable

public static class FusionCacheWrapperExtensions
{
    public static IServiceCollection AddCoreCacheServices(
        this IServiceCollection services,
        CacheConfiguration cacheConfiguration,
        IMemoryCache? memoryCache = null,
        Action<FusionCacheOptions>? configureFusionCacheOptions = null)
    {
        services.AddSingleton(cacheConfiguration);

        // Tests pass in their own MemoryCache so they can hold a reference for WipeL1(). When
        // not supplied, we create one here that's only ever reachable via FusionCache — it is
        // not registered in DI, so it can't shadow any IMemoryCache another consumer registers.
        memoryCache ??= new MemoryCache(new MemoryCacheOptions
        {
            // SizeLimit is in abstract units, not bytes. Each cache entry is assigned a Size value
            // (default 1). Compaction triggers when the sum of all entry sizes reaches SizeLimit.
            // With SizeLimit = 1_000_000, examples:
            //   - 1,000,000 small entries (size 1)
            //   - 100,000 medium entries (size 10)
            //   - 10,000 large entries (size 100)
            //   - or any combination summing to 1,000,000
            //
            SizeLimit = cacheConfiguration.MemoryCacheSizeLimit,
            CompactionPercentage = cacheConfiguration.MemoryCacheCompactionPercentage,
        });

        var builder = services.AddFusionCache()
            .WithOptions(options =>
            {
                // CUSTOM LOG LEVELS
                // options.FailSafeActivationLogLevel = LogLevel.Debug;
                // options.SerializationErrorsLogLevel = LogLevel.Warning;
                // options.DistributedCacheSyntheticTimeoutsLogLevel = LogLevel.Debug;
                // options.DistributedCacheErrorsLogLevel = LogLevel.Error;
                // options.FactorySyntheticTimeoutsLogLevel = LogLevel.Debug;
                // options.FactoryErrorsLogLevel = LogLevel.Error;

                configureFusionCacheOptions?.Invoke(options);
            })
            .WithMemoryCache(memoryCache)
            .WithDefaultEntryOptions(new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(1),
                // This is required to make the size-based eviction work, can be overriden on a per-entry basis
                Size = EntrySize.Small,

                // SEB:NOTE be careful setting this to true, since it can result in factories
                // being called in the background, which need to be handled carefully when the
                // factory needs to use a scoped db connection.
                IsFailSafeEnabled = false,
            })
            // SEB:TODO switch to FusionCacheNeueccMessagePackSerializer?
            .WithSerializer(new FusionCacheSystemTextJsonSerializer());

        if (cacheConfiguration.Level2CacheType == Level2CacheType.Redis)
        {
            builder
                .WithDistributedCache(sp =>
                {
                    var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
                    return new RedisCache(new RedisCacheOptions
                    {
                        ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer)
                    });
                })
                .WithBackplane(sp =>
                {
                    var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
                    return new RedisBackplane(new RedisBackplaneOptions
                    {
                        ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer)
                    });
                });
        }

        return services;
    }

    //

    public static ContainerBuilder AddSystemCaches(this ContainerBuilder cb)
    {
        cb.RegisterType<SystemLevel1Cache>().As<ISystemLevel1Cache>().SingleInstance();
        cb.RegisterType<SystemLevel2Cache>().As<ISystemLevel2Cache>().SingleInstance();
        cb.RegisterGeneric(typeof(SystemLevel1Cache<>)).As(typeof(ISystemLevel1Cache<>)).SingleInstance();
        cb.RegisterGeneric(typeof(SystemLevel2Cache<>)).As(typeof(ISystemLevel2Cache<>)).SingleInstance();
        return cb;
    }

    //

    public static ContainerBuilder AddTenantCaches(this ContainerBuilder cb, string odinCacheKeyPrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(odinCacheKeyPrefix, nameof(odinCacheKeyPrefix));

        cb.RegisterInstance(new CacheKeyPrefix(odinCacheKeyPrefix)).SingleInstance();

        cb.RegisterType<TenantLevel1Cache>().As<ITenantLevel1Cache>().SingleInstance();
        cb.RegisterType<TenantLevel2Cache>().As<ITenantLevel2Cache>().SingleInstance();
        cb.RegisterGeneric(typeof(TenantLevel1Cache<>)).As(typeof(ITenantLevel1Cache<>)).SingleInstance();
        cb.RegisterGeneric(typeof(TenantLevel2Cache<>)).As(typeof(ITenantLevel2Cache<>)).SingleInstance();

        return cb;
    }

}