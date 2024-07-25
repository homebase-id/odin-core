using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Background.Services.System;
using Odin.Services.Background.Services.Tenant;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Services.Background;

public static class BackgroundServiceExtensions
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
      
        cb.RegisterType<JobJanitorBackgroundService>()
            .AsSelf()
            .SingleInstance();
        
        cb.RegisterType<UpdateCertificatesBackgroundService>()
            .AsSelf()
            .SingleInstance();
        
        // Add more system services here
        // ...
        // ...
    }
    
    //
    
    public static async Task StartSystemBackgroundServices(this IBackgroundServiceManager bsm, IServiceProvider services)
    {
        // await bsm.StartAsync(nameof(DummySystemBackgroundService), services.GetRequiredService<DummySystemBackgroundService>());
        await bsm.StartAsync(nameof(JobJanitorBackgroundService), services.GetRequiredService<JobJanitorBackgroundService>());
        await bsm.StartAsync(nameof(UpdateCertificatesBackgroundService), services.GetRequiredService<UpdateCertificatesBackgroundService>());
       
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
        
        cb.RegisterType<InboxOutboxReconciliationBackgroundService>()
            .AsSelf()
            .SingleInstance();
        
        cb.RegisterType<PeerOutboxProcessorBackgroundService>()
            .AsSelf()
            .SingleInstance();
       
        // Add more tenant services here
        // ...
        // ...

    }
    
    //

    public static async Task StartTenantBackgroundServices(this IBackgroundServiceManager bsm, ILifetimeScope scope)
    {
        // await bsm.StartAsync("dummy-tenant-background-service", scope.Resolve<DummyTenantBackgroundService>());
        await bsm.StartAsync(nameof(PeerOutboxProcessorBackgroundService), scope.Resolve<PeerOutboxProcessorBackgroundService>());
        await bsm.StartAsync(nameof(InboxOutboxReconciliationBackgroundService), scope.Resolve<InboxOutboxReconciliationBackgroundService>());
    }
    
}
