using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Background.Certificate;
using Odin.Services.Background.DefaultCron;
using Odin.Services.JobManagement;

namespace Odin.Hosting.JobManagement;

public static class JobExtensions
{
    public static IServiceCollection AddCronJobs(this IServiceCollection services)
    {
        services.AddSingleton<DefaultCronSchedule>();
        services.AddSingleton<EnsureIdentityHasValidCertificateSchedule>();
        return services;
    }

    public static async Task ScheduleCronJobs(this IServiceProvider services)
    {
        var jobManager = services.GetRequiredService<IJobManager>();

        // DefaultCron
        {
            var scheduler = services.GetRequiredService<DefaultCronSchedule>();
            await jobManager.Delete(scheduler);
            await jobManager.Schedule<DefaultCronJob>(scheduler);
        }

        // EnsureIdentityHasValidCertificate
        {
            var scheduler = services.GetRequiredService<EnsureIdentityHasValidCertificateSchedule>();
            await jobManager.Delete(scheduler);
            await jobManager.Schedule<EnsureIdentityHasValidCertificateJob>(scheduler);
        }
    }

    public static async Task RemoveCronJobs(this IServiceProvider services)
    {
        var jobManager = services.GetRequiredService<IJobManager>();

        // DefaultCron
        {
            var scheduler = services.GetRequiredService<DefaultCronSchedule>();
            await jobManager.Delete(scheduler);
        }

        // EnsureIdentityHasValidCertificate
        {
            var scheduler = services.GetRequiredService<EnsureIdentityHasValidCertificateSchedule>();
            await jobManager.Delete(scheduler);
        }
    }
}