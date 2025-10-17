using System;
using Autofac;

namespace Odin.Core.Storage.PubSub;

public static class PubSubExtensions
{
    public static ContainerBuilder AddSystemPubSub(this ContainerBuilder cb, bool redisEnabled)
    {
        if (redisEnabled)
        {
            cb.RegisterType<SystemPubSub>().As<ISystemPubSub>().SingleInstance();
        }
        else
        {
            cb.RegisterType<NopPubSub>().As<ISystemPubSub>().SingleInstance();
        }
        return cb;
    }

    //

    public static ContainerBuilder AddTenantPubSub(this ContainerBuilder cb, bool redisEnabled, string channelPrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelPrefix, nameof(channelPrefix));

        cb.RegisterInstance(new ChannelPrefix(channelPrefix)).SingleInstance();

        if (redisEnabled)
        {
            cb.RegisterType<TenantPubSub>().As<ITenantPubSub>().SingleInstance();
        }
        else
        {
            cb.RegisterType<NopPubSub>().As<ITenantPubSub>().SingleInstance();
        }
        return cb;
    }
}
