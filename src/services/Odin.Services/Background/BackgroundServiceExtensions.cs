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
    public static void AddSystemBackgroundServices(this IServiceCollection services)
    {
        services.AddSingleton<IBackgroundServiceManager>(provider => new BackgroundServiceManager(
            provider.GetRequiredService<IServiceProvider>(),
            "system"
        ));

        // Background only services
        services.AddSingleton<DummySystemBackgroundService>();
        services.AddSingleton<JobCleanUpBackgroundService>();
        services.AddSingleton<JobRunnerBackgroundService>();
        services.AddSingleton<UpdateCertificatesBackgroundService>();
       
        // Add more system services here
        // ...
        // ...
    }
    
    //
    
    public static async Task StartSystemBackgroundServices(this IServiceProvider services)
    {
        var bsm = services.GetRequiredService<IBackgroundServiceManager>();
        
        // await bsm.StartAsync(nameof(DummySystemBackgroundService), services.GetRequiredService<DummySystemBackgroundService>());
        await bsm.StartAsync(nameof(JobCleanUpBackgroundService), services.GetRequiredService<JobCleanUpBackgroundService>());
        await bsm.StartAsync(nameof(JobRunnerBackgroundService), services.GetRequiredService<JobRunnerBackgroundService>());
        await bsm.StartAsync(nameof(UpdateCertificatesBackgroundService), services.GetRequiredService<UpdateCertificatesBackgroundService>());
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
       
        cb.RegisterType<IcrKeyAvailableBackgroundService>()
            .WithParameter(new TypedParameter(typeof(Tenant.Tenant), tenant))
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
    
        // await bsm.StartAsync("dummy-tenant-background-service", scope.Resolve<DummyTenantBackgroundService>());
        await bsm.StartAsync(nameof(PeerOutboxProcessorBackgroundService), scope.Resolve<PeerOutboxProcessorBackgroundService>());
        await bsm.StartAsync(nameof(InboxOutboxReconciliationBackgroundService), scope.Resolve<InboxOutboxReconciliationBackgroundService>());
        await bsm.StartAsync(nameof(IcrKeyAvailableBackgroundService), scope.Resolve<IcrKeyAvailableBackgroundService>());
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
