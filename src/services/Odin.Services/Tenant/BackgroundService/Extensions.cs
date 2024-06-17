using Autofac;
using Microsoft.Extensions.Logging;

namespace Odin.Services.Tenant.BackgroundService;

public static class Extensions
{
    public static void RegisterTenantBackgroundServices(this ContainerBuilder cb, Tenant tenant)
    {
        cb.Register(c => new TenantBackgroundServiceManager(
                c.Resolve<ILogger<TenantBackgroundServiceManager>>(),
                tenant))
            .As<ITenantBackgroundServiceManager>()
            .SingleInstance();

        cb.Register(c => new DummyBackgroundService(
                c.Resolve<ILogger<DummyBackgroundService>>(),
                tenant))
            .AsSelf()
            .SingleInstance();
    }
}
