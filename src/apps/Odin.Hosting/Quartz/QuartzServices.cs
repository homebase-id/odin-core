using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Configuration;
using Odin.Services.Quartz;
using Quartz;
using Quartz.AspNetCore;

namespace Odin.Hosting.Quartz;

public static class QuartzExtensions
{
    public static IServiceCollection AddQuartzServices(this IServiceCollection services, OdinConfiguration config)
    {
        services.Configure<QuartzOptions>(options =>
        {
            options.Scheduling.IgnoreDuplicates = true;
            options.Scheduling.OverWriteExistingData = true;
        });
        services.AddSingleton<JobListener>();
        services.AddSingleton<IJobManager, JobManager>();
        services.AddQuartz(q =>
        {
            q.UseDefaultThreadPool(tp =>
            {
                tp.MaxConcurrency = config.Quartz.MaxConcurrency;
            });
            q.AddJobListener<JobListener>();
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