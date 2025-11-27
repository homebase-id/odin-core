using System;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Background.BackgroundServices;
using Odin.Services.Background.BackgroundServices.System;
using Odin.Services.Background.BackgroundServices.Tenant;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Services.LastSeen;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Registry;
using Odin.Services.Security.Job;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Background;

public static class BackgroundServiceExtensions
{
    public static void AddSystemBackgroundServices(this ContainerBuilder cb, OdinConfiguration config)
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
        cb.RegisterBackgroundService<LastSeenBackgroundService>();
        cb.RegisterBackgroundService<LogTransactionalCacheStatsBackgroundService>();

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
        await bsm.StartAsync<LastSeenBackgroundService>();
        await bsm.StartAsync<LogTransactionalCacheStatsBackgroundService>();
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
        cb.RegisterBackgroundService<TempFolderCleanUpBackgroundService>();
        cb.RegisterBackgroundService<SecurityHealthCheckBackgroundScheduler>();

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
        await bsm.StartAsync<TempFolderCleanUpBackgroundService>();
        await bsm.StartAsync<SecurityHealthCheckBackgroundScheduler>();
        
    }
    
    //

    public static async Task ShutdownTenantBackgroundServices(this IServiceProvider services)
    {
        var multitenantContainer = services.GetRequiredService<IMultiTenantContainer>();
        var registry = services.GetRequiredService<IIdentityRegistry>();
        var registrations = registry.GetList().Result;
        foreach (var registration in registrations.Results)
        {
            var scope = multitenantContainer.GetTenantScope(registration.PrimaryDomainName);
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
