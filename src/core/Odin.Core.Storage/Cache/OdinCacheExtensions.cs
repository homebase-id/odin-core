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
                // SEB:TODO check if these are the correct defaults

                Duration = TimeSpan.FromMinutes(1),

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