using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Services.Background.Certificate;
using Odin.Core.Services.Background.DefaultCron;
using Odin.Core.Services.Quartz;

namespace Odin.Hosting.Quartz;

public static class Extensions
{
    public static IServiceCollection AddCronJobs(this IServiceCollection services)
    {
        services.AddSingleton<DefaultCronScheduler>();
        services.AddSingleton<EnsureIdentityHasValidCertificateScheduler>();
        return services;
    }

    public static async Task ScheduleCronJobs(this IServiceProvider services)
    {
        var jobManager = services.GetRequiredService<IJobManager>();

        // DefaultCron
        {
            var scheduler = services.GetRequiredService<DefaultCronScheduler>();
            await jobManager.Delete(scheduler.SchedulingKey);
            await jobManager.Schedule<DefaultCronJob>(scheduler);
        }

        // EnsureIdentityHasValidCertificate
        {
            var scheduler = services.GetRequiredService<EnsureIdentityHasValidCertificateScheduler>();
            await jobManager.Delete(scheduler.SchedulingKey);
            await jobManager.Schedule<EnsureIdentityHasValidCertificateJob>(scheduler);
        }
    }

}