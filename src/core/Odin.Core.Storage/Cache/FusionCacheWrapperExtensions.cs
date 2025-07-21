using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.NeueccMessagePack;

namespace Odin.Core.Storage.Cache;

#nullable enable

public static class FusionCacheWrapperExtensions
{
    public static IServiceCollection AddCoreCacheServices(
        this IServiceCollection services,
        CacheConfiguration cacheConfiguration)
    {
        services.AddSingleton(cacheConfiguration);

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
            })
            .WithDefaultEntryOptions(new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(1),

                // SEB:NOTE be careful setting this to true, since it can result in factories
                // being called in the background, which need to be handled carefully when the
                // factory needs to use a scoped db connection.
                IsFailSafeEnabled = false,
            })
            .WithSerializer(
                new FusionCacheNeueccMessagePackSerializer()
                // new FusionCacheSystemTextJsonSerializer()
            );

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