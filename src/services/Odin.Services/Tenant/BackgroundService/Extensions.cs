using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Services.Tenant.BackgroundService.Services;

namespace Odin.Services.Tenant.BackgroundService;

public static class Extensions
{
    public static void RegisterTenantBackgroundServices(this ContainerBuilder cb, Tenant tenant)
    {
        cb.RegisterType<TenantBackgroundServiceManager>()
            .WithParameter(new TypedParameter(typeof(Tenant), tenant))
            .As<ITenantBackgroundServiceManager>()
            .SingleInstance();

        cb.RegisterType<OutboxBackgroundService>()
            .WithParameter(new TypedParameter(typeof(Tenant), tenant))
            .AsSelf()
            .SingleInstance();

        cb.RegisterType<DummyBackgroundService>()
            .WithParameter(new TypedParameter(typeof(Tenant), tenant))
            .AsSelf()
            .SingleInstance();
    }
}
