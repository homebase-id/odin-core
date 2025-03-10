using System;
using Autofac;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
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
            );

        if (cacheConfiguration.Level2CacheType == Level2CacheType.Redis)
        {
            ArgumentException.ThrowIfNullOrEmpty(
                cacheConfiguration.Level2Configuration,
                nameof(cacheConfiguration.Level2Configuration));

            builder
                .WithDistributedCache(
                    new RedisCache(new RedisCacheOptions { Configuration = cacheConfiguration.Level2Configuration })
                )
                .WithBackplane(
                    new RedisBackplane(new RedisBackplaneOptions
                        { Configuration = cacheConfiguration.Level2Configuration })
                );
        }

        return services;
    }

    //

    public static ContainerBuilder AddGlobalCaches(this ContainerBuilder cb)
    {
        cb.RegisterType<GlobalLevel1Cache>().As<IGlobalLevel1Cache>().SingleInstance();
        cb.RegisterType<GlobalLevel2Cache>().As<IGlobalLevel2Cache>().SingleInstance();
        cb.RegisterGeneric(typeof(GlobalLevel1Cache<>)).As(typeof(IGlobalLevel1Cache<>)).SingleInstance();
        cb.RegisterGeneric(typeof(GlobalLevel2Cache<>)).As(typeof(IGlobalLevel2Cache<>)).SingleInstance();
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