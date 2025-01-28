using System;
using Autofac;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.NeueccMessagePack;

namespace Odin.Core.Storage.Cache;

#nullable enable

public static class OdinCacheExtensions
{
    public static IServiceCollection AddCoreCacheServices(
        this IServiceCollection services,
        OdinCacheOptions odinCacheOptions)
    {
        var builder = services.AddFusionCache()
            .WithOptions(options =>
            {
                options.DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(2);

                // CUSTOM LOG LEVELS
                // SEB:TODO check if these are the correct log levels
                options.FailSafeActivationLogLevel = LogLevel.Debug;
                options.SerializationErrorsLogLevel = LogLevel.Warning;
                options.DistributedCacheSyntheticTimeoutsLogLevel = LogLevel.Debug;
                options.DistributedCacheErrorsLogLevel = LogLevel.Error;
                options.FactorySyntheticTimeoutsLogLevel = LogLevel.Debug;
                options.FactoryErrorsLogLevel = LogLevel.Error;
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

        if (odinCacheOptions.Level2CacheType == Level2CacheType.Redis)
        {
            ArgumentException.ThrowIfNullOrEmpty(
                odinCacheOptions.Level2Configuration,
                nameof(odinCacheOptions.Level2Configuration));

            builder
                .WithDistributedCache(
                    new RedisCache(new RedisCacheOptions { Configuration = odinCacheOptions.Level2Configuration })
                )
                .WithBackplane(
                    new RedisBackplane(new RedisBackplaneOptions
                        { Configuration = odinCacheOptions.Level2Configuration })
                );
        }

        return services;
    }

    //

    public static ContainerBuilder AddOdinCache(this ContainerBuilder cb, string odinCacheKeyPrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(odinCacheKeyPrefix, nameof(odinCacheKeyPrefix));

        cb.RegisterInstance(new OdinCacheKeyPrefix(odinCacheKeyPrefix)).SingleInstance();
        cb.RegisterType<OdinCache>().As<IOdinCache>().SingleInstance();

        return cb;
    }
}