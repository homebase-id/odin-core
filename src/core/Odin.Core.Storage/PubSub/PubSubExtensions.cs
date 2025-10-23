using System;
using Autofac;

namespace Odin.Core.Storage.PubSub;

#nullable enable

public static class PubSubExtensions
{
    public static ContainerBuilder AddSystemPubSub(this ContainerBuilder cb, bool redisEnabled)
    {
        if (redisEnabled)
        {
            cb.RegisterType<SystemRedisPubSub>().As<ISystemPubSub>().SingleInstance();
        }
        else
        {
            cb.RegisterType<SystemInProcPubSub>().As<ISystemPubSub>().SingleInstance();
        }
        return cb;
    }

    //

    public static ContainerBuilder AddTenantPubSub(this ContainerBuilder cb, string channelPrefix, bool redisEnabled)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelPrefix, nameof(channelPrefix));

        cb.RegisterInstance(new ChannelPrefix(channelPrefix)).SingleInstance();

        if (redisEnabled)
        {
            cb.RegisterType<TenantRedisPubSub>().As<ITenantPubSub>().SingleInstance();
        }
        else
        {
            cb.RegisterType<TenantInProcPubSub>().As<ITenantPubSub>().SingleInstance();
        }
        return cb;
    }
}
