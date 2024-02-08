using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Services.Background.Certificate;
using Odin.Core.Services.Background.DefaultCron;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Quartz;
using Odin.Hosting.Quartz;
using Quartz;
using Quartz.AspNetCore;

namespace Odin.Hosting.Extensions;

public static class QuartzExtensions
{
    public static IServiceCollection AddQuartzServices(this IServiceCollection services, OdinConfiguration config)
    {
        services.Configure<QuartzOptions>(options =>
        {
            options.Scheduling.IgnoreDuplicates = true;
            options.Scheduling.OverWriteExistingData = true;
        });
        services.AddSingleton<IExclusiveJobManager, ExclusiveJobManager>(); // SEB:TODO Remove
        services.AddSingleton<JobListener>();
        services.AddSingleton<IJobManager, JobManager>();
        services.AddQuartz(q =>
        {
            q.UseDefaultThreadPool(tp =>
            {
                tp.MaxConcurrency = 256;
            });
            q.AddJobListener<JobListener>();
            if (config.Quartz.EnableQuartzBackgroundService)
            {
                q.UseDefaultCronSchedule(config);
                q.UseDefaultCertificateRenewalSchedule(config);
            }
            q.UsePersistentStore(storeOptions =>
            {
                var connectionString =
                    $"Data Source={Path.Combine(config.Host.SystemDataRootPath, config.Quartz.SqliteDatabaseFileName)}";

                QuartzSqlite.CreateSchema(connectionString);
                storeOptions.UseMicrosoftSQLite(sqliteOptions =>
                {
                    sqliteOptions.ConnectionString = connectionString;
                });

                storeOptions.UseProperties = true;
                storeOptions.UseNewtonsoftJsonSerializer(); // SEB:NOTE no support for System.Text.Json yet
            });
        });
        services.AddQuartzServer(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        return services;
    }
}