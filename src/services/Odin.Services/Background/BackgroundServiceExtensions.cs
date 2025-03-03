using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Background.Services;
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

        // cb.RegisterBackgroundService<DummySystemBackgroundService>();
        cb.RegisterBackgroundService<JobCleanUpBackgroundService>();
        cb.RegisterBackgroundService<JobRunnerBackgroundService>();
        cb.RegisterBackgroundService<UpdateCertificatesBackgroundService>();
        cb.RegisterBackgroundService<TenantTempCleanUpBackgroundService>();

        // Add more system background services here
        // ...
        // ...
    }
    
    //
    
    public static async Task StartSystemBackgroundServices(this IServiceProvider services)
    {
        var bsm = services.GetRequiredService<IBackgroundServiceManager>();
        
        // await bsm.StartAsync<DummySystemBackgroundService>(nameof(DummySystemBackgroundService));
        await bsm.StartAsync<JobCleanUpBackgroundService>();
        await bsm.StartAsync<JobRunnerBackgroundService>();
        await bsm.StartAsync<UpdateCertificatesBackgroundService>();
        await bsm.StartAsync<TenantTempCleanUpBackgroundService>();
    }

    //

    public static async Task ShutdownSystemBackgroundServices(this IServiceProvider services)
    {
        var bsm = services.GetRequiredService<IBackgroundServiceManager>();
        await bsm.ShutdownAsync();
    }
    
    //
    
    public static void AddTenantBackgroundServices(this ContainerBuilder cb, IdentityRegistration registration)
    {
        cb.RegisterType<BackgroundServiceManager>()
            .WithParameter(new TypedParameter(typeof(string), registration.PrimaryDomainName))
            .As<IBackgroundServiceManager>()
            .SingleInstance();

        // cb.RegisterBackgroundService<DummyTenantBackgroundService>();
        cb.RegisterBackgroundService<InboxOutboxReconciliationBackgroundService>();
        cb.RegisterBackgroundService<PeerOutboxProcessorBackgroundService>();

        // Add more tenant background services here
        // ...
        // ...
    }
    
    //

    public static async Task StartTenantBackgroundServices(this ILifetimeScope scope)
    {
        var bsm = scope.Resolve<IBackgroundServiceManager>();
    
        // await bsm.StartAsync<DummyTenantBackgroundService>("dummy-tenant-background-service");
        await bsm.StartAsync<PeerOutboxProcessorBackgroundService>();
        await bsm.StartAsync<InboxOutboxReconciliationBackgroundService>();
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

    private static ContainerBuilder RegisterBackgroundService<TService>(this ContainerBuilder cb)
        where TService : AbstractBackgroundService
    {
        cb.RegisterType<TService>()
            .AsSelf()
            .InstancePerDependency(); // Important!

        cb.RegisterType<BackgroundServiceTrigger<TService>>()
            .As<IBackgroundServiceTrigger<TService>>()
            .SingleInstance(); // Important!

        return cb;
    }
}
