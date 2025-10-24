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
            // The one and only InProcPubSubBroker for the entire system. Tenants do NOT have their own.
            cb.RegisterType<InProcPubSubBroker>().SingleInstance();
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
