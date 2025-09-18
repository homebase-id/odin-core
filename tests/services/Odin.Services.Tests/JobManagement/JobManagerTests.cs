using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.Hostname;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;
using Odin.Core.Tasks;
using Odin.Core.Time;
using Odin.Services.Background;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Tests.JobManagement.Jobs;
using Odin.Test.Helpers.Logging;
using Serilog.Events;
using Testcontainers.PostgreSql;

namespace Odin.Services.Tests.JobManagement;

public class JobManagerTests
{
    private ILifetimeScope _container = null!;
    private string? _tempPath;
    private IBackgroundServiceManager? _backgroundServiceManager;
    private JobCleanUpBackgroundService? _jobCleanUpBackgroundService;
    private JobRunnerBackgroundService? _jobRunnerBackgroundService;
    private PostgreSqlContainer? _postgresContainer;
    
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
        _container?.Dispose();
        _container = null!;

        _postgresContainer?.DisposeAsync().AsTask().Wait();
        _postgresContainer = null;

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
            BackgroundServices = new OdinConfiguration.BackgroundServicesSection
            {
                JobCleanUpIntervalSeconds = 120
            }
        };

        if (databaseType == DatabaseType.Postgres)
        {
            _postgresContainer = new PostgreSqlBuilder()
                .WithImage("postgres:latest")
                .WithDatabase("odin")
                .WithUsername("odin")
                .WithPassword("odin")
                .Build();
            await _postgresContainer.StartAsync();
        }

        var builder = new ContainerBuilder();
        builder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();

        builder.RegisterInstance(config);

        var logStore = new LogEventMemoryStore();
        builder.RegisterInstance(logStore).As<ILogEventMemoryStore>();
        builder.RegisterInstance(TestLogFactory.CreateLoggerFactory(logStore));
        builder.RegisterType<CorrelationUniqueIdGenerator>().As<ICorrelationIdGenerator>().SingleInstance();
        builder.RegisterType<CorrelationContext>().As<ICorrelationContext>().SingleInstance();
        builder.RegisterType<StickyHostnameGenerator>().As<IStickyHostnameGenerator>().SingleInstance();
        builder.RegisterType<StickyHostname>().As<IStickyHostname>().SingleInstance();
        builder.RegisterType<NodeLock>().As<INodeLock>().SingleInstance();

        builder.RegisterType<BackgroundServiceManager>()
            .WithParameter(new TypedParameter(typeof(string), "system"))
            .As<IBackgroundServiceManager>()
            .SingleInstance();

        builder.RegisterType<JobCleanUpBackgroundService>().InstancePerDependency();
        builder.RegisterType<JobRunnerBackgroundService>().InstancePerDependency();
        builder.RegisterType<BackgroundServiceTrigger<JobRunnerBackgroundService>>()
            .As<IBackgroundServiceTrigger<JobRunnerBackgroundService>>()
            .SingleInstance();

        builder.RegisterType<JobManager>().As<IJobManager>().InstancePerDependency();
        builder.RegisterType<ScopedJobTestDependency>().InstancePerLifetimeScope();

        var jobTypeRegistry = new JobTypeRegistry();
        builder.RegisterInstance(jobTypeRegistry).As<IJobTypeRegistry>();

        jobTypeRegistry.RegisterJobType<SimpleJobTest>(builder, SimpleJobTest.JobTypeId);
        jobTypeRegistry.RegisterJobType<SimpleJobWithDelayTest>(builder, SimpleJobWithDelayTest.JobTypeId);
        jobTypeRegistry.RegisterJobType<EventuallySucceedJobTest>(builder, EventuallySucceedJobTest.JobTypeId);
        jobTypeRegistry.RegisterJobType<AbortingJobTest>(builder, AbortingJobTest.JobTypeId);
        jobTypeRegistry.RegisterJobType<RescheduleJobTest>(builder, RescheduleJobTest.JobTypeId);
        jobTypeRegistry.RegisterJobType<RescheduleOnCancelJobTest>(builder, RescheduleOnCancelJobTest.JobTypeId);
        jobTypeRegistry.RegisterJobType<JobWithHashTest>(builder, JobWithHashTest.JobTypeId);
        jobTypeRegistry.RegisterJobType<FailingJobWithHashTest>(builder, FailingJobWithHashTest.JobTypeId);
        jobTypeRegistry.RegisterJobType<FailingJobTest>(builder, FailingJobTest.JobTypeId);
        jobTypeRegistry.RegisterJobType<ChainedJobTest>(builder, ChainedJobTest.JobTypeId);
        jobTypeRegistry.RegisterJobType<ScopedJobTest>(builder, ScopedJobTest.JobTypeId);

        builder.AddDatabaseServices();
        switch (databaseType)
        {
            case DatabaseType.Sqlite:
                builder.AddSqliteSystemDatabaseServices(Path.Combine(config.Host.SystemDataRootPath!, "sys.db"));
                break;
            case DatabaseType.Postgres:
                builder.AddPgsqlSystemDatabaseServices(_postgresContainer!.GetConnectionString());
                break;
            default:
                throw new OdinSystemException("Unsupported database type");
        }

        _container = builder.Build();

        var systemDatabase = _container.Resolve<SystemDatabase>();
        await systemDatabase.MigrateDatabaseAsync();

        _backgroundServiceManager = _container.Resolve<IBackgroundServiceManager>();
        _jobCleanUpBackgroundService = _backgroundServiceManager.Create<JobCleanUpBackgroundService>(nameof(JobCleanUpBackgroundService));
        _jobRunnerBackgroundService = _backgroundServiceManager.Create<JobRunnerBackgroundService>(nameof(JobRunnerBackgroundService));
    }
    
    //
    
    private void AssertLogEvents()
    {
        var logEvents = _container.Resolve<ILogEventMemoryStore>().GetLogEvents();
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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task GetCountAsyncShouldReturnZero(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();
        
        // Act
        var count = await jobManager.CountJobsAsync();
        
        // Assert
        Assert.That(count, Is.EqualTo(0));

        AssertLogEvents();
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldScheduleAJob(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();
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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldDeleteAJob(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();
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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldGetTheJob(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();
        
        var job = jobManager.NewJob<SimpleJobTest>();
        var jobId = await jobManager.ScheduleJobAsync(job);
        Assert.That(jobId, Is.Not.EqualTo(Guid.Empty));
        
        // Act
        var scheduledJob = await jobManager.GetJobAsync<SimpleJobTest>(jobId);
        
        // Assert
        Assert.That(scheduledJob, Is.Not.Null);
        ClassicAssert.AreEqual(job.JobData.SomeJobData, scheduledJob!.JobData.SomeJobData);
        ClassicAssert.AreEqual("SimpleJobTest", scheduledJob!.Record!.name);
        
        AssertLogEvents();
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldRunTheJobDirectly(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();
        
        var job = jobManager.NewJob<SimpleJobTest>();
        var jobId = await jobManager.ScheduleJobAsync(job);
        Assert.That(jobId, Is.Not.EqualTo(Guid.Empty));
        
        var scheduledJob = await jobManager.GetJobAsync<SimpleJobTest>(jobId);
        Assert.That(scheduledJob, Is.Not.Null);
        ClassicAssert.AreEqual(job.JobData.SomeJobData, scheduledJob!.JobData.SomeJobData);

        // Act
        await jobManager.RunJobNowAsync(jobId, CancellationToken.None);
        
        // Assert
        var completedJob = await jobManager.GetJobAsync<SimpleJobTest>(jobId);
        Assert.That(completedJob, Is.Not.Null);
        Assert.That(completedJob!.State, Is.EqualTo(JobState.Succeeded));
        ClassicAssert.AreEqual("hurrah!", completedJob!.JobData.SomeJobData);
        
        AssertLogEvents();
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldRunTheJobInTheBackground(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();
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
        ClassicAssert.AreEqual("hurrah!", completedJob!.JobData.SomeJobData);

        await StopBackgroundServices();
        AssertLogEvents();
    }
    
    //

#if !CI_GITHUB
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldRunManyParallelJobsInTheBackground(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobList = new List<Guid>();
        var jobManager = _container.Resolve<IJobManager>();
        var logger = _container.Resolve<ILogger<JobManagerTests>>();

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
            await WaitForJobStatus<SimpleJobWithDelayTest>(jobManager, jobId, JobState.Succeeded, TimeSpan.FromSeconds(5)); 
        }        
        
        // Assert
        foreach (var jobId in jobList)
        {
            var completedJob = await jobManager.GetJobAsync<SimpleJobWithDelayTest>(jobId);
            Assert.That(completedJob, Is.Not.Null);
            Assert.That(completedJob!.State, Is.EqualTo(JobState.Succeeded));
            ClassicAssert.AreEqual("hurrah!", completedJob!.JobData.SomeSerializedData);
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
        var jobManager = _container.Resolve<IJobManager>();

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
        var jobManager = _container.Resolve<IJobManager>();
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
        var jobManager = _container.Resolve<IJobManager>();

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
        
        var logEvents = _container.Resolve<ILogEventMemoryStore>().GetLogEvents();
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
        var jobManager = _container.Resolve<IJobManager>();
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
        await WaitForJobStatus<EventuallySucceedJobTest>(jobManager, jobId, JobState.Failed, TimeSpan.FromSeconds(3));

        // Assert final fail
        job = await jobManager.GetJobAsync<EventuallySucceedJobTest>(jobId);
        Assert.That(job, Is.Not.Null);
        Assert.That(job!.State, Is.EqualTo(JobState.Failed));
        Assert.That(job.JobData.RunCount, Is.EqualTo(maxAttempts));
        Assert.That(job.Record!.lastError, failUsingException
            ? Is.EqualTo("Fail with exception")
            : Is.EqualTo("unspecified error"));
        
        var logEvents = _container.Resolve<ILogEventMemoryStore>().GetLogEvents();
        Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(1), "Unexpected number of Error log events");
    }
    

    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldRunAndAbortDirectly(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();

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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldRunAndAbortInTheBackground(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();
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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldRescheduleDirectly(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();

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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldRescheduleInTheBackground(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();
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
        var jobManager = _container.Resolve<IJobManager>();

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
        var jobManager = _container.Resolve<IJobManager>();
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
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldScheduleUniqueJob(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();

        var dt1 = DateTimeOffset.UtcNow.AddMinutes(1);
        var schedule1 = new JobSchedule { RunAt = dt1 };

        var job1 = _container.Resolve<JobWithHashTest>();
        var jobId1 = await jobManager.ScheduleJobAsync(job1, schedule1);

        {
            var scheduleJob1 = await jobManager.GetJobAsync<JobWithHashTest>(jobId1);
            Assert.That(scheduleJob1!.Record!.nextRun.milliseconds, Is.EqualTo(dt1.ToUnixTimeMilliseconds()));
        }

        var dt2 = DateTimeOffset.UtcNow.AddMinutes(2);
        var schedule2 = new JobSchedule { RunAt = dt2 };

        var job2 = _container.Resolve<JobWithHashTest>();
        var jobId2 = await jobManager.ScheduleJobAsync(job2, schedule2);

        Assert.That(jobId1, Is.EqualTo(jobId2));

        {
            // Make sure the schedule didn't change
            var scheduleJob1 = await jobManager.GetJobAsync<JobWithHashTest>(jobId2);
            Assert.That(scheduleJob1!.Record!.nextRun.milliseconds, Is.EqualTo(dt1.ToUnixTimeMilliseconds()));
        }

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
        var jobManager = _container.Resolve<IJobManager>();

        var job1 = _container.Resolve<SimpleJobTest>();
        var jobId1 = await jobManager.ScheduleJobAsync(job1, new JobSchedule
        {
            OnSuccessDeleteAfter = TimeSpan.FromMilliseconds(deleteAfterMilliseconds)
        });

        var job2 = _container.Resolve<SimpleJobTest>();
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

#if !CI_GITHUB    
    [Test]
    [TestCase(DatabaseType.Sqlite, 0)]
    [TestCase(DatabaseType.Sqlite, 100)]
    public async Task ItShouldDeleteExpiredSuccessfulJobsInTheBackground(DatabaseType databaseType, int deleteAfterMilliseconds)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();
        var backgroundServiceManager = _container.Resolve<IBackgroundServiceManager>();

        await StartBackgroundServices();

        // Wait a bit so JobCleanUpBackgroundService has time to run its first cycle
        await Task.Delay(200);

        var job1 = _container.Resolve<SimpleJobTest>();
        var jobId1 = await jobManager.ScheduleJobAsync(job1, new JobSchedule
        {
            OnSuccessDeleteAfter = TimeSpan.FromMilliseconds(deleteAfterMilliseconds)
        });

        var job2 = _container.Resolve<SimpleJobTest>();
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
        await backgroundServiceManager.PulseBackgroundProcessorAsync(nameof(JobCleanUpBackgroundService));

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
        var jobManager = _container.Resolve<IJobManager>();

        var job1 = _container.Resolve<FailingJobTest>();
        var jobId1 = await jobManager.ScheduleJobAsync(job1, new JobSchedule
        {
            OnFailureDeleteAfter = TimeSpan.FromMilliseconds(deleteAfterMilliseconds)
        });

        var job2 = _container.Resolve<FailingJobTest>();
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

        var logEvents = _container.Resolve<ILogEventMemoryStore>().GetLogEvents();
        Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(2), "Unexpected number of Error log events");
    }

    //

#if !CI_GITHUB
    [Test]
    [TestCase(DatabaseType.Sqlite, 0)]
    [TestCase(DatabaseType.Sqlite, 100)]
    public async Task ItShouldDeleteExpiredUnsuccessfulJobsInTheBackground(DatabaseType databaseType, int deleteAfterMilliseconds)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();
        var backgroundServiceManager = _container.Resolve<IBackgroundServiceManager>();
        await StartBackgroundServices();

        // Wait a bit so JobCleanUpBackgroundService has time to run its first cycle
        await Task.Delay(200);

        var job1 = _container.Resolve<FailingJobTest>();
        var jobId1 = await jobManager.ScheduleJobAsync(job1, new JobSchedule
        {
            OnFailureDeleteAfter = TimeSpan.FromMilliseconds(deleteAfterMilliseconds)
        });

        var job2 = _container.Resolve<FailingJobTest>();
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
        await backgroundServiceManager.PulseBackgroundProcessorAsync(nameof(JobCleanUpBackgroundService));

        // Wait a bit so JobCleanUpBackgroundService has time to do its thing
        await Task.Delay(200);

        // Assert
        {
            var completedJob1 = await jobManager.GetJobAsync<FailingJobTest>(jobId1);
            Assert.That(completedJob1, Is.Null);
        }

        completedJob2 = await jobManager.GetJobAsync<FailingJobTest>(jobId2);
        Assert.That(completedJob2, Is.Not.Null);

        var logEvents = _container.Resolve<ILogEventMemoryStore>().GetLogEvents();
        Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(2), "Unexpected number of Error log events");
    }
#endif
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldRunAChainedJobDirectly(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();
        
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
        ClassicAssert.AreEqual("hurrah!", completedJob!.JobData.SomeJobData);
        
        AssertLogEvents();
    }
    
    //
    
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldRunAChainedJobInTheBackground(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();
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
        ClassicAssert.AreEqual("hurrah!", completedJob!.JobData.SomeJobData);

        await StopBackgroundServices();
        AssertLogEvents();
    }
    
    //

    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
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

        var scopedTestDependency = _container.Resolve<ScopedJobTestDependency>();
        scopedTestDependency.Value = "sanity";
        scopedTestDependency = _container.Resolve<ScopedJobTestDependency>();
        Assert.That(scopedTestDependency.Value, Is.EqualTo("sanity"));

        var jobManager = _container.Resolve<IJobManager>();
        await StartBackgroundServices();

        var scopedJob = jobManager.NewJob<ScopedJobTest>();
        var scopedJobId = await jobManager.ScheduleJobAsync(scopedJob);
        Assert.That(scopedJobId, Is.Not.EqualTo(Guid.Empty));

        // Act
        await WaitForJobStatus<ScopedJobTest>(jobManager, scopedJobId, JobState.Succeeded, TimeSpan.FromSeconds(1));

        scopedJob = await jobManager.GetJobAsync<ScopedJobTest>(scopedJobId);
        Assert.That(scopedJob!.JobData.ScopedTestCopy, Is.EqualTo("new born"));

        scopedTestDependency = _container.Resolve<ScopedJobTestDependency>();
        Assert.That(scopedTestDependency.Value, Is.EqualTo("sanity"));

        await StopBackgroundServices();
        AssertLogEvents();
    }


    //

#if !CI_GITHUB
    [Test]
    [TestCase(DatabaseType.Sqlite)]
    #if RUN_POSTGRES_TESTS
    [TestCase(DatabaseType.Postgres)]
    #endif
    public async Task ItShouldGoToTownOnScheduledUniqueJobs(DatabaseType databaseType)
    {
        // Arrange
        await CreateHostedJobManagerAsync(databaseType);
        var jobManager = _container.Resolve<IJobManager>();
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

        var job = _container.Resolve<FailingJobWithHashTest>();
        var jobId = await jobManager.ScheduleJobAsync(job, schedule);

        for (var idx = 0; idx < 100; idx++)
        {
            job = _container.Resolve<FailingJobWithHashTest>();
            await jobManager.ScheduleJobAsync(job, schedule);
            await Task.Delay(random.Next(1, 20));
        }

        await Task.Delay(1000);

        await StopBackgroundServices();

        var orphanedJobsCount = await jobManager.CountJobsAsync();

        Assert.That(orphanedJobsCount, Is.EqualTo(0));

        var logEvents = _container.Resolve<ILogEventMemoryStore>().GetLogEvents();
        foreach (var logEvent in logEvents[LogEventLevel.Error])
        {
            Assert.That(logEvent.RenderMessage(), Does.StartWith("JobManager giving up on unsuccessful job"));
        }
    }
#endif

    //

    private async Task WaitForJobStatus<T>(IJobManager jobManager, Guid jobId, JobState status, TimeSpan maxWaitTime) where T : AbstractJob
    {
        var logger = _container.Resolve<ILogger<JobManagerTests>>();
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