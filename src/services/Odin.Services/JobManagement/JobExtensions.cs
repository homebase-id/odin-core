using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Admin.Tenants.Jobs;
using Odin.Services.Registry.Registration;

namespace Odin.Services.JobManagement;

public static class JobExtensions
{
    public static IServiceCollection AddJobManagerServices(this IServiceCollection services)
    {
        services.AddSingleton<IJobManager, JobManager>();
        services.AddTransient<ExportTenantJob>();
        services.AddTransient<DeleteTenantJob>();
        services.AddTransient<SendProvisioningCompleteEmailJob>();
        return services;
    }
}