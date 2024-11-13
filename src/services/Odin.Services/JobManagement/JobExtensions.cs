using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Admin.Tenants.Jobs;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Membership.Connections.IcrKeyAvailableWorker;
using Odin.Services.Registry.Registration;

namespace Odin.Services.JobManagement;

public static class JobExtensions
{
    public static IServiceCollection AddJobManagerServices(this IServiceCollection services)
    {
        // SEB:NOTE JobManager has to be registered as a singleton
        // as it depends on the IBackgroundServiceTrigger that is registered in the root scope,
        // and "overwritten" in the tenant scopes. This is not ideal. JobManager should be transient. Fix it.
        services.AddSingleton<IJobManager, JobManager>();

        services.AddTransient<ExportTenantJob>();
        services.AddTransient<DeleteTenantJob>();
        services.AddTransient<SendProvisioningCompleteEmailJob>();
        services.AddTransient<VersionUpgradeJob>();
        services.AddTransient<IcrKeyAvailableJob>();

        return services;
    }
}