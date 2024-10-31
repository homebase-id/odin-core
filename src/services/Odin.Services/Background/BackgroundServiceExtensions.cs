using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Background.Services.System;
using Odin.Services.Background.Services.Tenant;
using Odin.Services.JobManagement;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Background;

public static class BackgroundServiceExtensions
{
    public static void AddSystemBackgroundServices(this ContainerBuilder cb)
    {
        // BackgroundServiceManager
        cb.RegisterType<BackgroundServiceManager>()
            .WithParameter(new TypedParameter(typeof(string), "system"))
            .As<IBackgroundServiceManager>()
            .SingleInstance();

        // Background only services
        cb.RegisterType<DummySystemBackgroundService>()
            .AsSelf()
            .SingleInstance();
        
        cb.RegisterType<JobCleanUpBackgroundService>()
            .AsSelf()
            .SingleInstance();
        
        cb.RegisterType<JobRunnerBackgroundService>()
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
    
    public static async Task StartSystemBackgroundServices(this IServiceProvider services)
    {
        var bsm = services.GetRequiredService<IBackgroundServiceManager>();
        
        // await bsm.StartAsync<DummySystemBackgroundService>(nameof(DummySystemBackgroundService));
        await bsm.StartAsync<JobCleanUpBackgroundService>(nameof(JobCleanUpBackgroundService));
        await bsm.StartAsync<JobRunnerBackgroundService>(nameof(JobRunnerBackgroundService));
        await bsm.StartAsync<UpdateCertificatesBackgroundService>(nameof(UpdateCertificatesBackgroundService));
    }
    
    //

    public static async Task ShutdownSystemBackgroundServices(this IServiceProvider services)
    {
        var bsm = services.GetRequiredService<IBackgroundServiceManager>();
        await bsm.ShutdownAsync();
    }
    
    //
    
    public static void AddTenantBackgroundServices(this ContainerBuilder cb, Tenant.Tenant tenant)
    {
        cb.RegisterType<BackgroundServiceManager>()
            .WithParameter(new TypedParameter(typeof(string), tenant.Name))
            .As<IBackgroundServiceManager>()
            .SingleInstance();

        cb.RegisterType<DummyTenantBackgroundService>()
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

    public static async Task StartTenantBackgroundServices(this ILifetimeScope scope)
    {
        var bsm = scope.Resolve<IBackgroundServiceManager>();
    
        // await bsm.StartAsync<DummyTenantBackgroundService>("dummy-tenant-background-service");
        await bsm.StartAsync<PeerOutboxProcessorBackgroundService>(nameof(PeerOutboxProcessorBackgroundService));
        await bsm.StartAsync<InboxOutboxReconciliationBackgroundService>(nameof(InboxOutboxReconciliationBackgroundService));
    }
    
    //

    public static async Task ShutdownTenantBackgroundServices(this IServiceProvider services)
    {
        var multitenantContainer = services.GetRequiredService<IMultiTenantContainerAccessor>();
        var registry = services.GetRequiredService<IIdentityRegistry>();
        var registrations = registry.GetList().Result;
        foreach (var registration in registrations.Results)
        {
            var scope = multitenantContainer.Container().GetTenantScope(registration.PrimaryDomainName);
            var backgroundServiceManager = scope.Resolve<IBackgroundServiceManager>();
            await backgroundServiceManager.ShutdownAsync();
        }
    }
    
    //
    
}
