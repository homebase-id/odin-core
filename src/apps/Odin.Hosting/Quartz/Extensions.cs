using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Background.Certificate;
using Odin.Services.Background.DefaultCron;
using Odin.Services.Quartz;
using Quartz;

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

    public static async Task RemoveCronJobs(this IServiceProvider services)
    {
        var jobManager = services.GetRequiredService<IJobManager>();

        // DefaultCron
        {
            var scheduler = services.GetRequiredService<DefaultCronScheduler>();
            await jobManager.Delete(scheduler.SchedulingKey);
        }

        // EnsureIdentityHasValidCertificate
        {
            var scheduler = services.GetRequiredService<EnsureIdentityHasValidCertificateScheduler>();
            await jobManager.Delete(scheduler.SchedulingKey);
        }
    }

    public static async Task GracefullyStopAllQuartzSchedulers(this IServiceProvider services)
    {
        var schedulerFactory = services.GetRequiredService<ISchedulerFactory>();
        var schedulers = await schedulerFactory.GetAllSchedulers();
        foreach (var scheduler in schedulers)
        {
            await scheduler.Shutdown(true);
        }
    }

}