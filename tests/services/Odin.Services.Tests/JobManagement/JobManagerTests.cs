using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.Hostname;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Tasks;
using Odin.Services.Background;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Services.Tests.JobManagement.Jobs;
using Odin.Test.Helpers.Logging;
using Serilog.Events;

namespace Odin.Services.Tests.JobManagement;

#nullable enable

public class JobManagerTests
{
    private IHost? _host;
    private string? _tempPath;
    private IBackgroundServiceManager? _backgroundServiceManager;
    private JobCleanUpBackgroundService? _jobCleanUpBackgroundService;
    private JobRunnerBackgroundService? _jobRunnerBackgroundService;
    
    [SetUp]
    public void Setup()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
    }

    //

    [TearDown]
    public void TearDown()
    {
        _backgroundServiceManager?.ShutdownAsync().BlockingWait();
        _host?.Dispose();
        _host = null;
        if (!string.IsNullOrEmpty(_tempPath))
        {
            Directory.Delete(_tempPath, true);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
    
    //

    #region Helpers
    
    private async Task CreateHostedJobManagerAsync(DatabaseType databaseType)
    {
        var config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                SystemDataRootPath = _tempPath
            },
            Job = new OdinConfiguration.JobSection
            {
                JobCleanUpIntervalSeconds = 120
            }
        };

        _host = Host.CreateDefaultBuilder()
            .UseServiceProviderFactory(new AutofacServiceProviderFactory()) // Use Autofac as DI container
            .ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton(config);
                
                var logStore = new LogEventMemoryStore();
                services.AddSingleton<ILogEventMemoryStore>(logStore);
                services.AddSingleton(TestLogFactory.CreateLoggerFactory(logStore));
                services.AddSingleton<ICorrelationIdGenerator, CorrelationUniqueIdGenerator>();
                services.AddSingleton<ICorrelationContext, CorrelationContext>();
                services.AddSingleton<IStickyHostnameGenerator, StickyHostnameGenerator>();
                services.AddSingleton<IStickyHostname, StickyHostname>();

                //
                //  Background services
                //

                services.AddSingleton<IBackgroundServiceManager>(provider => new BackgroundServiceManager(
                    provider.GetRequiredService<ILifetimeScope>(),
                    "system"
                ));

                services.AddTransient<JobCleanUpBackgroundService>();
                services.AddTransient<JobRunnerBackgroundService>();
                services.AddSingleton<
                    IBackgroundServiceTrigger<JobRunnerBackgroundService>,
                    BackgroundServiceTrigger<JobRunnerBackgroundService>>();

                //
                // Jobs
                //

                services.AddTransient<IJobManager, JobManager>();

                services.AddTransient<SimpleJobTest>();
                services.AddTransient<SimpleJobWithDelayTest>();
                services.AddTransient<EventuallySucceedJobTest>();
                services.AddTransient<AbortingJobTest>();
                services.AddTransient<RescheduleJobTest>();
                services.AddTransient<RescheduleOnCancelJobTest>();
                services.AddTransient<JobWithHashTest>();
                services.AddTransient<FailingJobWithHashTest>();
                services.AddTransient<FailingJobTest>();
                services.AddTransient<ChainedJobTest>();
                services.AddTransient<ScopedJobTest>(); services.AddScoped<ScopedJobTestDependency>();
            })
            .ConfigureContainer<ContainerBuilder>((hostContext, builder) =>
            {
                builder.AddDatabaseCacheServices();
                builder.AddDatabaseCounterServices();
                switch (databaseType)
                {
                    case DatabaseType.Sqlite:
                        builder.AddSqliteSystemDatabaseServices(Path.Combine(config.Host.SystemDataRootPath!, "sys.db"));
                        break;
                    case DatabaseType.Postgres:
                        builder.AddPgsqlSystemDatabaseServices(config.Database.ConnectionString);
                        break;
                    default:
                        throw new OdinSystemException("Unsupported database type");
                }
            })
            .Build();

        var systemDatabase = _host.Services.GetRequiredService<SystemDatabase>();
        await systemDatabase.CreateDatabaseAsync(true);

        _backgroundServiceManager = _host.Services.GetRequiredService<IBackgroundServiceManager>();
        _jobCleanUpBackgroundService = _backgroundServiceManager.Create<JobCleanUpBackgroundService>(nameof(JobCleanUpBackgroundService));
        _jobRunnerBackgroundService = _backgroundServiceManager.Create<JobRunnerBackgroundService>(nameof(JobRunnerBackgroundService));
    }
    
    //
    
    private void AssertLogEvents()
    {
        var logEvents = _host!.Services.GetRequiredService<ILogEventMemoryStore>().GetLogEvents();
        LogEvents.AssertEvents(logEvents);
    }
    
    //
    
    private async Task StartBackgroundServices()
    {
        await _backgroundServiceManager!.StartAsync(_jobCleanUpBackgroundService!);
        await _backgroundServiceManager!.StartAsync(_jobRunnerBackgroundService!);
    }
    
    //

    private async Task StopBackgroundServices()
    {
        await _backgroundServiceManager!.StopAsync(nameof(JobRunnerBackgroundService));
        await _backgroundServiceManager!.StopAsync(nameof(JobCleanUpBackgroundService));
    }
    
    #endregion    
    
    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task GetCountAsyncShouldReturnZero(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        
        // Act
        var count = await jobManager.CountJobsAsync();
        
        // Assert
        Assert.That(count, Is.EqualTo(0));

        AssertLogEvents();
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldScheduleAJob(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        var jobCount = await jobManager.CountJobsAsync();
        Assert.That(jobCount, Is.EqualTo(0));
        var job = jobManager.NewJob<SimpleJobTest>();

        // Act
        var schedule = new JobSchedule
        {
            RunAt = DateTimeOffset.Now.AddSeconds(1),
            MaxAttempts = 123,
            RetryDelay = TimeSpan.FromSeconds(321),
            OnSuccessDeleteAfter = TimeSpan.FromSeconds(10),
            OnFailureDeleteAfter = TimeSpan.FromSeconds(20),
        };
        var jobId = await jobManager.ScheduleJobAsync(job, schedule);
        
        // Assert
        Assert.That(jobId, Is.Not.EqualTo(Guid.Empty));

        jobCount = await jobManager.CountJobsAsync();
        Assert.That(jobCount, Is.EqualTo(1));

        var scheduledJob = await jobManager.GetJobAsync<SimpleJobTest>(jobId);
        Assert.That(scheduledJob, Is.Not.Null);
        Assert.That(scheduledJob!.Record!.name, Is.EqualTo("SimpleJobTest"));
        Assert.That(scheduledJob.Record!.state, Is.EqualTo((int)JobState.Scheduled));
        Assert.That(scheduledJob.Record!.priority, Is.EqualTo(int.MaxValue / 2));
        Assert.That(scheduledJob.Record!.nextRun.milliseconds, Is.EqualTo(schedule.RunAt.ToUnixTimeMilliseconds()));
        Assert.That(scheduledJob.Record!.lastRun, Is.Null);
        Assert.That(scheduledJob.Record!.runCount, Is.EqualTo(0));
        Assert.That(scheduledJob.Record!.maxAttempts, Is.EqualTo(schedule.MaxAttempts));
        Assert.That(scheduledJob.Record!.retryDelay, Is.EqualTo(schedule.RetryDelay.TotalMilliseconds));
        Assert.That(scheduledJob.Record!.onSuccessDeleteAfter, Is.EqualTo(schedule.OnSuccessDeleteAfter.TotalMilliseconds));
        Assert.That(scheduledJob.Record!.onFailureDeleteAfter, Is.EqualTo(schedule.OnFailureDeleteAfter.TotalMilliseconds));
        Assert.That(scheduledJob.Record!.expiresAt, Is.Null);

        AssertLogEvents();
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldDeleteAJob(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        var jobCount = await jobManager.CountJobsAsync();
        Assert.That(jobCount, Is.EqualTo(0));
        
        var job = jobManager.NewJob<SimpleJobTest>();
        var jobId = await jobManager.ScheduleJobAsync(job);
        Assert.That(jobId, Is.Not.EqualTo(Guid.Empty));

        var exists = await jobManager.JobExistsAsync(jobId);
        Assert.That(exists, Is.True);
        
        jobCount = await jobManager.CountJobsAsync();
        Assert.That(jobCount, Is.EqualTo(1));

        // Act
        var deleted = await jobManager.DeleteJobAsync(jobId);
        
        // Assert
        Assert.That(deleted, Is.True);
        
        jobCount = await jobManager.CountJobsAsync();
        Assert.That(jobCount, Is.EqualTo(0));
        
        exists = await jobManager.JobExistsAsync(jobId);
        Assert.That(exists, Is.False);
        
        AssertLogEvents();
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldGetTheJob(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        
        var job = jobManager.NewJob<SimpleJobTest>();
        var jobId = await jobManager.ScheduleJobAsync(job);
        Assert.That(jobId, Is.Not.EqualTo(Guid.Empty));
        
        // Act
        var scheduledJob = await jobManager.GetJobAsync<SimpleJobTest>(jobId);
        
        // Assert
        Assert.That(scheduledJob, Is.Not.Null);
        Assert.AreEqual(job.JobData.SomeJobData, scheduledJob!.JobData.SomeJobData);
        Assert.AreEqual("SimpleJobTest", scheduledJob!.Record!.name);
        
        AssertLogEvents();
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldRunTheJobDirectly(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        
        var job = jobManager.NewJob<SimpleJobTest>();
        var jobId = await jobManager.ScheduleJobAsync(job);
        Assert.That(jobId, Is.Not.EqualTo(Guid.Empty));
        
        var scheduledJob = await jobManager.GetJobAsync<SimpleJobTest>(jobId);
        Assert.That(scheduledJob, Is.Not.Null);
        Assert.AreEqual(job.JobData.SomeJobData, scheduledJob!.JobData.SomeJobData);

        // Act
        await jobManager.RunJobNowAsync(jobId, CancellationToken.None);
        
        // Assert
        var completedJob = await jobManager.GetJobAsync<SimpleJobTest>(jobId);
        Assert.That(completedJob, Is.Not.Null);
        Assert.That(completedJob!.State, Is.EqualTo(JobState.Succeeded));
        Assert.AreEqual("hurrah!", completedJob!.JobData.SomeJobData);
        
        AssertLogEvents();
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldRunTheJobInTheBackground(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        await StartBackgroundServices();
        
        var job = jobManager.NewJob<SimpleJobTest>();
        var jobId = await jobManager.ScheduleJobAsync(job);
        Assert.That(jobId, Is.Not.EqualTo(Guid.Empty));

        var scheduledJob = await jobManager.GetJobAsync<SimpleJobTest>(jobId);
        Assert.That(scheduledJob, Is.Not.Null);

        // Act
        await WaitForJobStatus<SimpleJobTest>(jobManager, jobId, JobState.Succeeded, TimeSpan.FromSeconds(1));
        
        // Assert
        var completedJob = await jobManager.GetJobAsync<SimpleJobTest>(jobId);
        Assert.That(completedJob, Is.Not.Null);
        Assert.That(completedJob!.State, Is.EqualTo(JobState.Succeeded));
        Assert.AreEqual("hurrah!", completedJob!.JobData.SomeJobData);

        await StopBackgroundServices();
        AssertLogEvents();
    }
    
    //

#if !NOISY_NEIGHBOUR
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldRunManyParallelJobsInTheBackground(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobList = new List<Guid>();
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        var logger = _host!.Services.GetRequiredService<ILogger<JobManagerTests>>();

        for (var idx = 0; idx < 200; idx++)
        {
            var job = jobManager.NewJob<SimpleJobWithDelayTest>();
            job.JobData.Delay = TimeSpan.FromMilliseconds(100);
            job.JobData.SomeOtherData = idx.ToString();
            var jobId = await jobManager.ScheduleJobAsync(job);
            jobList.Add(jobId);
        }

        // NOTE: moving this to before the job scheduling seems to trigger a rather hefty lock convoy on the sqlite level
        // and the performance drops significantly.
        await StartBackgroundServices();

        // Act
        foreach (var jobId in jobList)
        {
            await WaitForJobStatus<SimpleJobWithDelayTest>(jobManager, jobId, JobState.Succeeded, TimeSpan.FromSeconds(1)); 
        }        
        
        // Assert
        foreach (var jobId in jobList)
        {
            var completedJob = await jobManager.GetJobAsync<SimpleJobWithDelayTest>(jobId);
            Assert.That(completedJob, Is.Not.Null);
            Assert.That(completedJob!.State, Is.EqualTo(JobState.Succeeded));
            Assert.AreEqual("hurrah!", completedJob!.JobData.SomeSerializedData);
        }

        await StopBackgroundServices();
        
        AssertLogEvents();
    }
#endif    
    
    //

    [Test]
    [TestCase(DatabaseType.Sqlite, true, 1)]
    [TestCase(DatabaseType.Sqlite, true, 3)]
    [TestCase(DatabaseType.Sqlite, true, 30)]
    [TestCase(DatabaseType.Sqlite, false, 1)]
    [TestCase(DatabaseType.Sqlite, false, 3)]
    [TestCase(DatabaseType.Sqlite, false, 30)]
    public async Task ItShouldRunAndFailAndSucceedDirectly(DatabaseType databaseType, bool failUsingException, int succeedAtRun)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();

        var job = jobManager.NewJob<EventuallySucceedJobTest>();
        job.JobData = new EventuallySucceedJobTestData
        {
            FailUsingException = failUsingException,
            SucceedAfterRuns = succeedAtRun
        };

        var jobId = await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            MaxAttempts = succeedAtRun,
            RetryDelay = TimeSpan.Zero
        });

        job = await jobManager.GetJobAsync<EventuallySucceedJobTest>(jobId);
        Assert.That(job!.State, Is.EqualTo(JobState.Scheduled));
        Assert.That(job.JobData.RunCount, Is.EqualTo(0));

        // Assert intermediate fails
        for (var idx = 0; idx < succeedAtRun - 1; idx++)
        {
            await jobManager.RunJobNowAsync(jobId, CancellationToken.None);
            job = await jobManager.GetJobAsync<EventuallySucceedJobTest>(jobId);
            Assert.That(job, Is.Not.Null);
            Assert.That(job!.State, Is.EqualTo(JobState.Scheduled));
            Assert.That(job.JobData.RunCount, Is.EqualTo(idx + 1));
            Assert.That(job.Record!.lastError, failUsingException
                ? Is.EqualTo("Fail with exception")
                : Is.EqualTo("unspecified error"));
        }

        // Assert final success
        await jobManager.RunJobNowAsync(jobId, CancellationToken.None);
        job = await jobManager.GetJobAsync<EventuallySucceedJobTest>(jobId);
        Assert.That(job, Is.Not.Null);
        Assert.That(job!.State, Is.EqualTo(JobState.Succeeded));
        Assert.That(job.JobData.RunCount, Is.EqualTo(succeedAtRun));
        Assert.That(job.Record!.lastError, Is.Null);
        
        AssertLogEvents();
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite, true, 1)]
    [TestCase(DatabaseType.Sqlite, true, 2)]
    [TestCase(DatabaseType.Sqlite, true, 3)]
    [TestCase(DatabaseType.Sqlite, true, 10)]
    [TestCase(DatabaseType.Sqlite, false, 1)]
    [TestCase(DatabaseType.Sqlite, false, 2)]
    [TestCase(DatabaseType.Sqlite, false, 3)]
    [TestCase(DatabaseType.Sqlite, false, 10)]
    public async Task ItShouldRunAndFailAndSucceedInTheBackground(DatabaseType databaseType, bool failUsingException, int succeedAtRun)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        await StartBackgroundServices();

        var job = jobManager.NewJob<EventuallySucceedJobTest>();
        job.JobData = new EventuallySucceedJobTestData
        {
            FailUsingException = failUsingException,
            SucceedAfterRuns = succeedAtRun
        };

        var jobId = await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            MaxAttempts = succeedAtRun,
            RetryDelay = TimeSpan.Zero
        });

        // Act
        await WaitForJobStatus<EventuallySucceedJobTest>(jobManager, jobId, JobState.Succeeded, TimeSpan.FromSeconds(2));
        
        // Assert final success
        job = await jobManager.GetJobAsync<EventuallySucceedJobTest>(jobId);
        Assert.That(job, Is.Not.Null);
        Assert.That(job!.State, Is.EqualTo(JobState.Succeeded));
        Assert.That(job.JobData.RunCount, Is.EqualTo(succeedAtRun));
        Assert.That(job.Record!.lastError, Is.Null);
        
        AssertLogEvents();
    }
    

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, true, 1)]
    [TestCase(DatabaseType.Sqlite, true, 3)]
    [TestCase(DatabaseType.Sqlite, true, 30)]
    [TestCase(DatabaseType.Sqlite, false, 1)]
    [TestCase(DatabaseType.Sqlite, false, 3)]
    [TestCase(DatabaseType.Sqlite, false, 30)]
    public async Task ItShouldRunAndFailAndGiveUpDirectly(DatabaseType databaseType, bool failUsingException, int maxAttempts)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();

        var job = jobManager.NewJob<EventuallySucceedJobTest>();
        job.JobData = new EventuallySucceedJobTestData
        {
            FailUsingException = failUsingException,
            SucceedAfterRuns = maxAttempts + 1
        };

        var jobId = await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            MaxAttempts = maxAttempts,
            RetryDelay = TimeSpan.Zero
        });

        job = await jobManager.GetJobAsync<EventuallySucceedJobTest>(jobId);
        Assert.That(job!.State, Is.EqualTo(JobState.Scheduled));
        Assert.That(job.JobData.RunCount, Is.EqualTo(0));

        // Assert intermediate fails
        for (var idx = 0; idx < maxAttempts - 1; idx++)
        {
            await jobManager.RunJobNowAsync(jobId, CancellationToken.None);
            job = await jobManager.GetJobAsync<EventuallySucceedJobTest>(jobId);
            Assert.That(job, Is.Not.Null);
            Assert.That(job!.State, Is.EqualTo(JobState.Scheduled));
            Assert.That(job.JobData.RunCount, Is.EqualTo(idx + 1));
            Assert.That(job.Record!.lastError, failUsingException
                ? Is.EqualTo("Fail with exception")
                : Is.EqualTo("unspecified error"));
        }

        // Assert final fail
        await jobManager.RunJobNowAsync(jobId, CancellationToken.None);
        job = await jobManager.GetJobAsync<EventuallySucceedJobTest>(jobId);
        Assert.That(job, Is.Not.Null);
        Assert.That(job!.State, Is.EqualTo(JobState.Failed));
        Assert.That(job.JobData.RunCount, Is.EqualTo(maxAttempts));
        Assert.That(job.Record!.lastError, failUsingException
            ? Is.EqualTo("Fail with exception")
            : Is.EqualTo("unspecified error"));
        
        var logEvents = _host!.Services.GetRequiredService<ILogEventMemoryStore>().GetLogEvents();
        Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(1), "Unexpected number of Error log events");
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite, true, 1)]
    [TestCase(DatabaseType.Sqlite, true, 3)]
    [TestCase(DatabaseType.Sqlite, true, 10)]
    [TestCase(DatabaseType.Sqlite, false, 1)]
    [TestCase(DatabaseType.Sqlite, false, 3)]
    [TestCase(DatabaseType.Sqlite, false, 10)]
    public async Task ItShouldRunAndFailAndGiveUpInTheBackground(DatabaseType databaseType, bool failUsingException, int maxAttempts)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        await StartBackgroundServices();

        var job = jobManager.NewJob<EventuallySucceedJobTest>();
        job.JobData = new EventuallySucceedJobTestData
        {
            FailUsingException = failUsingException,
            SucceedAfterRuns = maxAttempts + 1
        };

        var jobId = await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            MaxAttempts = maxAttempts,
            RetryDelay = TimeSpan.Zero
        });
        
        // Act
        await WaitForJobStatus<EventuallySucceedJobTest>(jobManager, jobId, JobState.Failed, TimeSpan.FromSeconds(2));

        // Assert final fail
        job = await jobManager.GetJobAsync<EventuallySucceedJobTest>(jobId);
        Assert.That(job, Is.Not.Null);
        Assert.That(job!.State, Is.EqualTo(JobState.Failed));
        Assert.That(job.JobData.RunCount, Is.EqualTo(maxAttempts));
        Assert.That(job.Record!.lastError, failUsingException
            ? Is.EqualTo("Fail with exception")
            : Is.EqualTo("unspecified error"));
        
        var logEvents = _host!.Services.GetRequiredService<ILogEventMemoryStore>().GetLogEvents();
        Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(1), "Unexpected number of Error log events");
    }
    

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldRunAndAbortDirectly(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();

        var job = jobManager.NewJob<AbortingJobTest>();
        var jobId = await jobManager.ScheduleJobAsync(job);

        // Act
        await jobManager.RunJobNowAsync(jobId, CancellationToken.None);

        // Assert
        var exists = await jobManager.JobExistsAsync(jobId);
        Assert.That(exists, Is.False);

        AssertLogEvents();
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldRunAndAbortInTheBackground(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        await StartBackgroundServices();

        var job = jobManager.NewJob<AbortingJobTest>();
        var jobId = await jobManager.ScheduleJobAsync(job);

        // Act
        await Task.Delay(200);

        // Assert
        var exists = await jobManager.JobExistsAsync(jobId);
        Assert.That(exists, Is.False);

        AssertLogEvents();
    }

    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldRescheduleDirectly(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();

        var job = jobManager.NewJob<RescheduleJobTest>();

        var jobId = await jobManager.ScheduleJobAsync(job);

        // Act
        await jobManager.RunJobNowAsync(jobId, CancellationToken.None);

        var rescheduledJob = await jobManager.GetJobAsync<RescheduleJobTest>(jobId);
        Assert.That(rescheduledJob, Is.Not.Null);
        Assert.That(rescheduledJob!.Id, Is.EqualTo(jobId));
        Assert.That(rescheduledJob!.State, Is.EqualTo(JobState.Scheduled));
        Assert.That(rescheduledJob!.Record!.runCount, Is.EqualTo(0));
        Assert.That(rescheduledJob.Record!.nextRun.milliseconds, 
            Is.EqualTo(new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds()));

        AssertLogEvents();
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldRescheduleInTheBackground(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        await StartBackgroundServices();

        var job = jobManager.NewJob<RescheduleJobTest>();

        var jobId = await jobManager.ScheduleJobAsync(job);

        // Act
        await Task.Delay(200);

        var rescheduledJob = await jobManager.GetJobAsync<RescheduleJobTest>(jobId);
        Assert.That(rescheduledJob, Is.Not.Null);
        Assert.That(rescheduledJob!.Id, Is.EqualTo(jobId));
        Assert.That(rescheduledJob!.State, Is.EqualTo(JobState.Scheduled));
        Assert.That(rescheduledJob!.Record!.runCount, Is.EqualTo(0));
        Assert.That(rescheduledJob.Record!.nextRun.milliseconds, 
            Is.EqualTo(new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds()));

        AssertLogEvents();
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, true)]
    [TestCase(DatabaseType.Sqlite, false)]
    public async Task ItShouldRescheduleOnOperationCancelledDirectly(DatabaseType databaseType, bool cancelUsingException)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();

        var job = jobManager.NewJob<RescheduleOnCancelJobTest>();
        job.JobData = new RescheduleOnCancelJobTestData
        {
            CancelUsingException = cancelUsingException
        };

        var jobId = await jobManager.ScheduleJobAsync(job);

        // Act
        await jobManager.RunJobNowAsync(jobId, CancellationToken.None);

        job = await jobManager.GetJobAsync<RescheduleOnCancelJobTest>(jobId);
        Assert.That(job, Is.Not.Null);
        Assert.That(job!.State, Is.EqualTo(JobState.Scheduled));
        Assert.That(job!.Record!.runCount, Is.EqualTo(0));
        Assert.That(job.Record!.lastError, cancelUsingException
            ? Is.EqualTo("The operation was canceled.")
            : Is.EqualTo("job was rescheduled"));

        AssertLogEvents();
    }

    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite, true)]
    [TestCase(DatabaseType.Sqlite, false)]
    public async Task ItShouldRescheduleOnOperationCancelledInTheBackground(DatabaseType databaseType, bool cancelUsingException)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        await StartBackgroundServices();

        var job = jobManager.NewJob<RescheduleOnCancelJobTest>();
        job.JobData = new RescheduleOnCancelJobTestData
        {
            CancelUsingException = cancelUsingException
        };

        var jobId = await jobManager.ScheduleJobAsync(job);

        // Act
        await Task.Delay(200);

        job = await jobManager.GetJobAsync<RescheduleOnCancelJobTest>(jobId);
        Assert.That(job, Is.Not.Null);
        Assert.That(job!.State, Is.EqualTo(JobState.Scheduled));
        Assert.That(job!.Record!.runCount, Is.EqualTo(0));
        Assert.That(job.Record!.lastError, cancelUsingException
            ? Is.EqualTo("The operation was canceled.")
            : Is.EqualTo("job was rescheduled"));

        AssertLogEvents();
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldScheduleUniqueJob(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();

        var job1 = _host!.Services.GetRequiredService<JobWithHashTest>();
        var jobId1 = await jobManager.ScheduleJobAsync(job1);

        // Act
        var job2 = _host!.Services.GetRequiredService<JobWithHashTest>();
        var jobId2 = await jobManager.ScheduleJobAsync(job2);

        Assert.That(jobId1, Is.EqualTo(jobId2));

        AssertLogEvents();
    }

    //

    [Test]
    [TestCase(DatabaseType.Sqlite, 0)]
    [TestCase(DatabaseType.Sqlite, 100)]
    public async Task ItShouldDeleteExpiredSuccessfulJobsDirectly(DatabaseType databaseType, int deleteAfterMilliseconds)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();

        var job1 = _host!.Services.GetRequiredService<SimpleJobTest>();
        var jobId1 = await jobManager.ScheduleJobAsync(job1, new JobSchedule
        {
            OnSuccessDeleteAfter = TimeSpan.FromMilliseconds(deleteAfterMilliseconds)
        });

        var job2 = _host!.Services.GetRequiredService<SimpleJobTest>();
        var jobId2 = await jobManager.ScheduleJobAsync(job2, new JobSchedule
        {
            OnSuccessDeleteAfter = TimeSpan.FromDays(1)
        });

        await jobManager.RunJobNowAsync(jobId1, CancellationToken.None);

        // Assert JobManager deletes the job immediately if deleteAfterMilliseconds is 0
        if (deleteAfterMilliseconds == 0)
        {
            var completedJob1 = await jobManager.GetJobAsync<SimpleJobTest>(jobId1);
            Assert.That(completedJob1, Is.Null);
        }
        else
        {
            var completedJob1 = await jobManager.GetJobAsync<SimpleJobTest>(jobId1);
            Assert.That(completedJob1, Is.Not.Null);
        }

        await jobManager.RunJobNowAsync(jobId2, CancellationToken.None);
        var completedJob2 = await jobManager.GetJobAsync<SimpleJobTest>(jobId2);
        Assert.That(completedJob2, Is.Not.Null);
        
        await Task.Delay(2 * deleteAfterMilliseconds);
        
        // Act
        await jobManager.DeleteExpiredJobsAsync();

        // Assert
        {
            var completedJob1 = await jobManager.GetJobAsync<SimpleJobTest>(jobId1);
            Assert.That(completedJob1, Is.Null);
        }

        completedJob2 = await jobManager.GetJobAsync<SimpleJobTest>(jobId2);
        Assert.That(completedJob2, Is.Not.Null);

        AssertLogEvents();
    }

    //

#if !NOISY_NEIGHBOUR    
    [Test]
    [TestCase(DatabaseType.Sqlite, 0)]
    [TestCase(DatabaseType.Sqlite, 100)]
    public async Task ItShouldDeleteExpiredSuccessfulJobsInTheBackground(DatabaseType databaseType, int deleteAfterMilliseconds)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        var backgroundServiceManager = _host!.Services.GetRequiredService<IBackgroundServiceManager>();

        await StartBackgroundServices();

        // Wait a bit so JobCleanUpBackgroundService has time to run its first cycle
        await Task.Delay(200);

        var job1 = _host!.Services.GetRequiredService<SimpleJobTest>();
        var jobId1 = await jobManager.ScheduleJobAsync(job1, new JobSchedule
        {
            OnSuccessDeleteAfter = TimeSpan.FromMilliseconds(deleteAfterMilliseconds)
        });

        var job2 = _host!.Services.GetRequiredService<SimpleJobTest>();
        var jobId2 = await jobManager.ScheduleJobAsync(job2, new JobSchedule
        {
            OnSuccessDeleteAfter = TimeSpan.FromDays(1)
        });
        
        await Task.Delay(200);        

        // Assert JobManager deletes the job immediately if deleteAfterMilliseconds is 0
        if (deleteAfterMilliseconds == 0)
        {
            await Task.Delay(100);
            var completedJob1 = await jobManager.GetJobAsync<SimpleJobTest>(jobId1);
            Assert.That(completedJob1, Is.Null);
        }
        else
        {
            await WaitForJobStatus<SimpleJobTest>(jobManager, jobId1, JobState.Succeeded, TimeSpan.FromSeconds(2));
        }
        
        await WaitForJobStatus<SimpleJobTest>(jobManager, jobId2, JobState.Succeeded, TimeSpan.FromSeconds(2));

        if (deleteAfterMilliseconds > 0)
        {
            var completedJob1 = await jobManager.GetJobAsync<SimpleJobTest>(jobId1);
            Assert.That(completedJob1, Is.Not.Null);
        }

        var completedJob2 = await jobManager.GetJobAsync<SimpleJobTest>(jobId2);
        Assert.That(completedJob2, Is.Not.Null);

        await Task.Delay(200);
        
        // Act
        backgroundServiceManager.PulseBackgroundProcessor(nameof(JobCleanUpBackgroundService));

        // Wait a bit so JobCleanUpBackgroundService has time to do its thing
        await Task.Delay(200);

        // Assert
        {
            var completedJob1 = await jobManager.GetJobAsync<SimpleJobTest>(jobId1);
            Assert.That(completedJob1, Is.Null);
        }

        completedJob2 = await jobManager.GetJobAsync<SimpleJobTest>(jobId2);
        Assert.That(completedJob2, Is.Not.Null);

        AssertLogEvents();
    }
#endif
    
    //

    [Test]
    [TestCase(DatabaseType.Sqlite, 0)]
    [TestCase(DatabaseType.Sqlite, 100)]
    public async Task ItShouldDeleteExpiredUnsuccessfulJobsDirectly(DatabaseType databaseType, int deleteAfterMilliseconds)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();

        var job1 = _host!.Services.GetRequiredService<FailingJobTest>();
        var jobId1 = await jobManager.ScheduleJobAsync(job1, new JobSchedule
        {
            OnFailureDeleteAfter = TimeSpan.FromMilliseconds(deleteAfterMilliseconds)
        });

        var job2 = _host!.Services.GetRequiredService<FailingJobTest>();
        var jobId2 = await jobManager.ScheduleJobAsync(job2, new JobSchedule
        {
            OnFailureDeleteAfter = TimeSpan.FromDays(1)
        });

        await jobManager.RunJobNowAsync(jobId1, CancellationToken.None);
        
        // Assert JobManager deletes the job immediately if deleteAfterMilliseconds is 0
        if (deleteAfterMilliseconds == 0)
        {
            var completedJob1 = await jobManager.GetJobAsync<FailingJobTest>(jobId1);
            Assert.That(completedJob1, Is.Null);
        }
        else
        {
            var completedJob1 = await jobManager.GetJobAsync<FailingJobTest>(jobId1);
            Assert.That(completedJob1, Is.Not.Null);
        }

        await jobManager.RunJobNowAsync(jobId2, CancellationToken.None);
        var completedJob2 = await jobManager.GetJobAsync<FailingJobTest>(jobId2);
        Assert.That(completedJob2, Is.Not.Null);

        await Task.Delay(2 * deleteAfterMilliseconds);
        
        // Act
        await jobManager.DeleteExpiredJobsAsync();

        // Assert
        {
            var completedJob1 = await jobManager.GetJobAsync<FailingJobTest>(jobId1);
            Assert.That(completedJob1, Is.Null);
        }
        
        completedJob2 = await jobManager.GetJobAsync<FailingJobTest>(jobId2);
        Assert.That(completedJob2, Is.Not.Null);

        var logEvents = _host!.Services.GetRequiredService<ILogEventMemoryStore>().GetLogEvents();
        Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(2), "Unexpected number of Error log events");
    }

    //

#if !NOISY_NEIGHBOUR
    [Test]
    [TestCase(DatabaseType.Sqlite, 0)]
    [TestCase(DatabaseType.Sqlite, 100)]
    public async Task ItShouldDeleteExpiredUnsuccessfulJobsInTheBackground(DatabaseType databaseType, int deleteAfterMilliseconds)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        var backgroundServiceManager = _host!.Services.GetRequiredService<IBackgroundServiceManager>();
        await StartBackgroundServices();

        // Wait a bit so JobCleanUpBackgroundService has time to run its first cycle
        await Task.Delay(200);

        var job1 = _host!.Services.GetRequiredService<FailingJobTest>();
        var jobId1 = await jobManager.ScheduleJobAsync(job1, new JobSchedule
        {
            OnFailureDeleteAfter = TimeSpan.FromMilliseconds(deleteAfterMilliseconds)
        });

        var job2 = _host!.Services.GetRequiredService<FailingJobTest>();
        var jobId2 = await jobManager.ScheduleJobAsync(job2, new JobSchedule
        {
            OnFailureDeleteAfter = TimeSpan.FromDays(1)
        });
        
        // Assert JobManager deletes the job immediately if deleteAfterMilliseconds is 0
        if (deleteAfterMilliseconds == 0)
        {
            await Task.Delay(100);
            var completedJob1 = await jobManager.GetJobAsync<FailingJobTest>(jobId1);
            Assert.That(completedJob1, Is.Null);
        }
        else
        {
            await WaitForJobStatus<FailingJobTest>(jobManager, jobId1, JobState.Failed, TimeSpan.FromSeconds(1));
        }
        
        await WaitForJobStatus<FailingJobTest>(jobManager, jobId2, JobState.Failed, TimeSpan.FromSeconds(1));

        if (deleteAfterMilliseconds > 0)
        {
            var completedJob1 = await jobManager.GetJobAsync<FailingJobTest>(jobId1);
            Assert.That(completedJob1, Is.Not.Null);
        }

        var completedJob2 = await jobManager.GetJobAsync<FailingJobTest>(jobId2);
        Assert.That(completedJob2, Is.Not.Null);

        await Task.Delay(200);
        
        // Act
        backgroundServiceManager.PulseBackgroundProcessor(nameof(JobCleanUpBackgroundService));

        // Wait a bit so JobCleanUpBackgroundService has time to do its thing
        await Task.Delay(200);

        // Assert
        {
            var completedJob1 = await jobManager.GetJobAsync<FailingJobTest>(jobId1);
            Assert.That(completedJob1, Is.Null);
        }

        completedJob2 = await jobManager.GetJobAsync<FailingJobTest>(jobId2);
        Assert.That(completedJob2, Is.Not.Null);

        var logEvents = _host!.Services.GetRequiredService<ILogEventMemoryStore>().GetLogEvents();
        Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(2), "Unexpected number of Error log events");
    }
#endif
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldRunAChainedJobDirectly(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        
        var chainedJob = jobManager.NewJob<ChainedJobTest>();
        var chainedJobId = await jobManager.ScheduleJobAsync(chainedJob);
        Assert.That(chainedJobId, Is.Not.EqualTo(Guid.Empty));

        // Act
        await jobManager.RunJobNowAsync(chainedJobId, CancellationToken.None);
        chainedJob = await jobManager.GetJobAsync<ChainedJobTest>(chainedJobId);
        var simpleJobId = chainedJob!.JobData.SimpleJobId!.Value;
        await jobManager.RunJobNowAsync(simpleJobId, CancellationToken.None);

        // Assert
        var completedJob = await jobManager.GetJobAsync<SimpleJobTest>(simpleJobId);
        Assert.That(completedJob, Is.Not.Null);
        Assert.That(completedJob!.State, Is.EqualTo(JobState.Succeeded));
        Assert.AreEqual("hurrah!", completedJob!.JobData.SomeJobData);
        
        AssertLogEvents();
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldRunAChainedJobInTheBackground(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        await StartBackgroundServices();
        
        var chainedJob = jobManager.NewJob<ChainedJobTest>();
        var chainedJobId = await jobManager.ScheduleJobAsync(chainedJob);
        Assert.That(chainedJobId, Is.Not.EqualTo(Guid.Empty));

        // Act
        await WaitForJobStatus<ChainedJobTest>(jobManager, chainedJobId, JobState.Succeeded, TimeSpan.FromSeconds(1));

        chainedJob = await jobManager.GetJobAsync<ChainedJobTest>(chainedJobId);
        var simpleJobId = chainedJob!.JobData.SimpleJobId!.Value;

        await WaitForJobStatus<SimpleJobTest>(jobManager, simpleJobId, JobState.Succeeded, TimeSpan.FromSeconds(1));

        // Assert
        var completedJob = await jobManager.GetJobAsync<SimpleJobTest>(simpleJobId);
        Assert.That(completedJob, Is.Not.Null);
        Assert.That(completedJob!.State, Is.EqualTo(JobState.Succeeded));
        Assert.AreEqual("hurrah!", completedJob!.JobData.SomeJobData);

        await StopBackgroundServices();
        AssertLogEvents();
    }
    
    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task JobShouldHaveInternalChildDiScope(DatabaseType databaseType)
    {
        //
        // NOTE:
        //
        // To see this test fail because of wrong scope:
        // - go to JobManager::RunJobNowAsync
        // - change the lines
        //     using var job = await GetJobAsync<AbstractJob>(jobId, childScope);
        //   to
        //     var job = await GetJobAsync<AbstractJob>(jobId);
        //   this will cause the test to fail because the ScopedTestDependency will be resolved from the parent scope
        //   and not the child scope.
        //

        // Arrange
        await CreateHostedJobManagerAsync(databaseType);

        var scopedTestDependency = _host!.Services.GetRequiredService<ScopedJobTestDependency>();
        scopedTestDependency.Value = "sanity";
        scopedTestDependency = _host!.Services.GetRequiredService<ScopedJobTestDependency>();
        Assert.That(scopedTestDependency.Value, Is.EqualTo("sanity"));

        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        await StartBackgroundServices();

        var scopedJob = jobManager.NewJob<ScopedJobTest>();
        var scopedJobId = await jobManager.ScheduleJobAsync(scopedJob);
        Assert.That(scopedJobId, Is.Not.EqualTo(Guid.Empty));

        // Act
        await WaitForJobStatus<ScopedJobTest>(jobManager, scopedJobId, JobState.Succeeded, TimeSpan.FromSeconds(1));

        scopedJob = await jobManager.GetJobAsync<ScopedJobTest>(scopedJobId);
        Assert.That(scopedJob!.JobData.ScopedTestCopy, Is.EqualTo("new born"));

        scopedTestDependency = _host!.Services.GetRequiredService<ScopedJobTestDependency>();
        Assert.That(scopedTestDependency.Value, Is.EqualTo("sanity"));

        await StopBackgroundServices();
        AssertLogEvents();
    }


    //

#if !NOISY_NEIGHBOUR
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    public async Task ItShouldGoToTownOnScheduledUniqueJobs(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        await StartBackgroundServices();
        var random = new Random();

        var schedule = new JobSchedule
        {
            RunAt = DateTimeOffset.Now,
            MaxAttempts = 5,
            RetryDelay = TimeSpan.FromMilliseconds(3),
            OnSuccessDeleteAfter = TimeSpan.FromSeconds(0),
            OnFailureDeleteAfter = TimeSpan.FromSeconds(0),
        };

        var job = _host!.Services.GetRequiredService<FailingJobWithHashTest>();
        var jobId = await jobManager.ScheduleJobAsync(job, schedule);

        for (var idx = 0; idx < 100; idx++)
        {
            job = _host!.Services.GetRequiredService<FailingJobWithHashTest>();
            await jobManager.ScheduleJobAsync(job, schedule);
            await Task.Delay(random.Next(1, 20));
        }

        await Task.Delay(1000);

        await StopBackgroundServices();

        var orphanedJobsCount = await jobManager.CountJobsAsync();

        Assert.That(orphanedJobsCount, Is.EqualTo(0));

        var logEvents = _host!.Services.GetRequiredService<ILogEventMemoryStore>().GetLogEvents();
        foreach (var logEvent in logEvents[LogEventLevel.Error])
        {
            Assert.That(logEvent.RenderMessage(), Does.StartWith("JobManager giving up on unsuccessful job"));
        }
    }
#endif

    //

    private async Task WaitForJobStatus<T>(IJobManager jobManager, Guid jobId, JobState status, TimeSpan maxWaitTime) where T : AbstractJob
    {
        var logger = _host!.Services.GetRequiredService<ILogger<JobManagerTests>>();
        var sw = Stopwatch.StartNew();
        while (true)
        {
            logger.LogInformation($"> {jobId}");
            var job = await jobManager.GetJobAsync<T>(jobId) ?? throw new Exception("Test job not found");
            logger.LogInformation($"< {jobId}");
            if (job.State == status)
            {
                break;
            }
            if (sw.Elapsed > maxWaitTime)
            {
                throw new TimeoutException(
                    $"Job did not reach status {status} within {maxWaitTime}. Last status: {job.State}");
            }
            await Task.Delay(100);
        }
    }
}