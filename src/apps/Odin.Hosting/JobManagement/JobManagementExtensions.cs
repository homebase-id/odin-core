using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Quartz;
using Quartz.Spi;

namespace Odin.Hosting.JobManagement;

public static class JobManagementExtensions
{
    public static IServiceCollection AddJobManagementServices(this IServiceCollection services, OdinConfiguration config)
    {
        services.AddSingleton(new JobManagerConfig
        {
            ConnectionPooling = config.Job.ConnectionPooling,
            DatabaseDirectory = Path.Combine(config.Host.SystemDataRootPath, "jobs"),
            SchedulerThreadCount = config.Job.MaxSchedulerConcurrency
        });

        services.AddSingleton<IJobFactory, DiJobFactory>();
        services.AddSingleton<IJobListener, JobListener>();
        services.AddSingleton<IJobMemoryCache, JobMemoryCache>();
        services.AddSingleton<IJobManager, JobManager>();

        return services;
    }

    //

}