using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            ConnectionPooling = config.Quartz.ConnectionPooling,
            DatabaseDirectory = Path.Combine(config.Host.SystemDataRootPath, "jobs"),
            SchedulerThreadCount = config.Quartz.MaxConcurrency
        });

        services.AddSingleton<IJobFactory, DiJobFactory>();
        services.AddSingleton<IJobListener, JobListener>();
        services.AddSingleton<IJobManager, JobManager>();

        return services;
    }

    //

    public static IServiceProvider InitializeJobManagementServices(this IServiceProvider services)
    {
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        Quartz.Logging.LogContext.SetCurrentLogProvider(loggerFactory);
        return services;
    }

    public static IApplicationBuilder InitializeJobManagementServices(this IApplicationBuilder app)
    {
        app.ApplicationServices.InitializeJobManagementServices();
        return app;
    }


    //

}