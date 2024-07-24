using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Services.JobManagement;
using Quartz;
using Quartz.Impl;
using Quartz.Logging;
using Quartz.Spi;

namespace Odin.Hosting.Tests.JobManagement;

public class OldQuartzMultiSchedulerTests
{
    [Test]
    [Explicit]
    public async Task Test1()
    {
        var serviceCollection = new ServiceCollection();
        OldQuartzMultiScheduler.RegisterJobs(serviceCollection);
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var ms = new OldQuartzMultiScheduler(serviceProvider);

        var s1 = await ms.Start("s1");
        var s2 = await ms.Start("s2");

        await ms.ScheduleJob(s1);
        await ms.ScheduleJob(s2);

        await Task.Delay(10000);

        await s1.Shutdown(true);
        await s2.Shutdown(true);

        Assert.Pass();
    }

}

public class OldQuartzMultiScheduler(ServiceProvider serviceProvider)
{
    public static void RegisterJobs(ServiceCollection services)
    {
        services.AddTransient<HelloJob>();
        services.AddSingleton<Foo>();
        services.AddSingleton<IJobFactory, AspNetCoreJobFactory>();
    }

    public async Task<IScheduler> Start(string name)
    {
        LogProvider.SetCurrentLogProvider(new ConsoleLogProvider());

        const string databaseProvider = "SQLite-Microsoft";

        var connectionString = $"/Users/seb/tmp/xxx/{name}.db";

        OldQuartzSqlite.CreateSchema(connectionString);

        var properties = new NameValueCollection()
        {
            // https://www.quartz-scheduler.net/documentation/quartz-3.x/configuration/reference.html
            ["quartz.scheduler.instanceName"] = name,
            ["quartz.serializer.type"] = "json",
            ["quartz.jobStore.useProperties"] = "true",
            ["quartz.jobStore.dataSource"] = name,
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
            [$"quartz.dataSource.{name}.connectionString"] = connectionString,
            [$"quartz.dataSource.{name}.provider"] = databaseProvider,
            ["quartz.threadPool.threadCount"] = "1",
        };


        // Grab the Scheduler instance from the Factory
        var factory = new StdSchedulerFactory(properties);
        var scheduler = await factory.GetScheduler();
        scheduler.JobFactory = serviceProvider.GetRequiredService<IJobFactory>();

        // and start it off
        await scheduler.Start();

        return scheduler;
    }

    //

    public async Task ScheduleJob(IScheduler scheduler)
    {
        // define the job and tie it to our HelloJob class
        var job = JobBuilder.Create<HelloJob>()
            //.WithIdentity("job1", "group1")
            .Build();

        var trigger = TriggerBuilder.Create()
            //.WithIdentity("trigger1", "group1")
            .StartNow()
            //.StartAt(DateTimeOffset.Now + TimeSpan.FromHours(1))
            .Build();

        // Tell Quartz to schedule the job using our trigger
        await scheduler.ScheduleJob(job, trigger);
    }

    //

}

public class Foo
{
    public string Bar => "Bar";
}

public class HelloJob(Foo foo) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await Console.Out.WriteLineAsync("[" + DateTime.Now.ToLongTimeString() + "] " + $"Sleeping...{foo.Bar}");
        await Task.Delay(1000);
        await Console.Out.WriteLineAsync("[" + DateTime.Now.ToLongTimeString() + "] " + "Awake!");
    }
}


class ConsoleLogProvider : ILogProvider
{
    public Logger GetLogger(string name)
    {
        return (level, func, exception, parameters) =>
        {
            if (level >= LogLevel.Info && func != null)
            {
                Console.WriteLine("[" + DateTime.Now.ToLongTimeString() + "] [" + level + "] " + func(), parameters);
            }
            return true;
        };
    }

    public IDisposable OpenNestedContext(string message)
    {
        throw new NotImplementedException();
    }

    public IDisposable OpenMappedContext(string key, object value, bool destructure = false)
    {
        throw new NotImplementedException();
    }
}


public class AspNetCoreJobFactory(IServiceProvider serviceProvider) : IJobFactory
{
    public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        return (IJob)serviceProvider.GetRequiredService(bundle.JobDetail.JobType);
    }

    public void ReturnJob(IJob job)
    {
        // Handle job return, if needed.
    }
}