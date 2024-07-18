using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Background.Services.System;
using Odin.Services.Background.Services.Tenant;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Services.Background;

public static class Extensions
{
    public static void RegisterSystemBackgroundServices(this ContainerBuilder cb)
    {
        cb.RegisterType<BackgroundServiceManager>()
            .WithParameter(new TypedParameter(typeof(string), "system"))
            .As<IBackgroundServiceManager>()
            .SingleInstance();
        
        cb.RegisterType<DummySystemBackgroundService>()
            .AsSelf()
            .SingleInstance();
      
        
        // Add more system services here
        // ...
        // ...
    }
    
    //
    
    public static async Task StartSystemBackgroundServices(this IBackgroundServiceManager bsm, IServiceProvider services)
    {
        // await bsm.StartAsync("dummy-system-background-service", services.GetRequiredService<DummySystemBackgroundService>());
        await Task.CompletedTask;
    }
    
    //
    
    public static void RegisterTenantBackgroundServices(this ContainerBuilder cb, Tenant.Tenant tenant)
    {
        cb.RegisterType<BackgroundServiceManager>()
            .WithParameter(new TypedParameter(typeof(string), tenant.Name))
            .As<IBackgroundServiceManager>()
            .SingleInstance();

        cb.RegisterType<DummyTenantBackgroundService>()
            .WithParameter(new TypedParameter(typeof(Tenant.Tenant), tenant))
            .AsSelf()
            .SingleInstance();
        
        // SEB:TODO also registered in startup/DI. Fix it.
        // cb.RegisterType<PeerOutboxProcessorAsync>()
        //     .AsSelf()
        //     .SingleInstance();
       
        // Add more tenant services here
        // ...
        // ...

    }
    
    //

    public static async Task StartTenantBackgroundServices(this IBackgroundServiceManager bsm, ILifetimeScope scope)
    {
        // await bsm.StartAsync("dummy-tenant-background-service", scope.Resolve<DummyTenantBackgroundService>());
        await bsm.StartAsync(nameof(PeerOutboxProcessorAsync), scope.Resolve<PeerOutboxProcessorAsync>());
    }
    
}
