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
            .As<IBackgroundServiceTrigger>()
            .As<IBackgroundServiceManager>()
            .SingleInstance();

        // Background only services
        cb.RegisterType<DummySystemBackgroundService>()
            .AsSelf()
            .InstancePerDependency();

        cb.RegisterType<JobCleanUpBackgroundService>()
            .AsSelf()
            .InstancePerDependency();

        cb.RegisterType<JobRunnerBackgroundService>()
            .AsSelf()
            .InstancePerDependency();

        cb.RegisterType<UpdateCertificatesBackgroundService>()
            .AsSelf()
            .InstancePerDependency();

        // Add more system services here
        // They MUST be InstancePerDependency
        // because they are using scopes internally
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
    
    public static void AddTenantBackgroundServices(this ContainerBuilder cb, IdentityRegistration registration)
    {
        cb.RegisterType<BackgroundServiceManager>()
            .WithParameter(new TypedParameter(typeof(string), registration.PrimaryDomainName))
            .As<IBackgroundServiceTrigger>()
            .As<IBackgroundServiceManager>()
            .SingleInstance();

        cb.RegisterType<DummyTenantBackgroundService>()
            .AsSelf()
            .InstancePerDependency();
        
        cb.RegisterType<InboxOutboxReconciliationBackgroundService>()
            .AsSelf()
            .InstancePerDependency();
        
        cb.RegisterType<PeerOutboxProcessorBackgroundService>()
            .AsSelf()
            .InstancePerDependency();

        // Add more tenant services here
        // They MUST be InstancePerDependency
        // because they are using scopes internally
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
