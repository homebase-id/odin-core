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
using NUnit.Framework;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.Hostname;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Storage.SQLite.ServerDatabase;
using Odin.Core.Tasks;
using Odin.Services.Background;
using Odin.Services.Base;
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
    
    [SetUp]
    public void Setup()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
        
        _host = CreateHostedJobManager();
        _backgroundServiceManager = _host.Services.GetRequiredService<IBackgroundServiceManager>();
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
    }
    
    //

    #region Helpers
    
    private IHost CreateHostedJobManager()
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

        var host = Host.CreateDefaultBuilder()
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
                services.AddSingleton<ServerSystemStorage>();
            
                services.AddSingleton<IBackgroundServiceManager>(provider => new BackgroundServiceManager(
                    provider.GetRequiredService<ILifetimeScope>(),
                    "system"
                ));

                services.AddSingleton<JobCleanUpBackgroundService>();
                services.AddSingleton<JobRunnerBackgroundService>();
                services.AddSingleton<IJobManager, JobManager>();
                
                services.AddTransient<SimpleJobTest>();
                services.AddTransient<SimpleJobWithDelayTest>();
                services.AddTransient<EventuallySucceedJobTest>();
                services.AddTransient<AbortingJobTest>();
                services.AddTransient<RescheduleJobTest>();
                services.AddTransient<RescheduleOnCancelJobTest>();
                services.AddTransient<JobWithHash>();
                services.AddTransient<FailingJobTest>();
                services.AddTransient<ChainedJobTest>();
            })
            .Build();

        return host;
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
        await _backgroundServiceManager!.StartAsync<JobCleanUpBackgroundService>(nameof(JobCleanUpBackgroundService));
        await _backgroundServiceManager!.StartAsync<JobRunnerBackgroundService>(nameof(JobRunnerBackgroundService));
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
    public async Task GetCountAsyncShouldReturnZero()
    {
        // Arrange
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        
        // Act
        var count = await jobManager.CountJobsAsync();
        
        // Assert
        Assert.That(count, Is.EqualTo(0));

        AssertLogEvents();
    }
    
    //
    
    [Test]
    public async Task ItShouldScheduleAJob()
    {
        // Arrange
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
    public async Task ItShouldDeleteAJob()
    {
        // Arrange
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
    public async Task ItShouldGetTheJob()
    {
        // Arrange
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
    public async Task ItShouldRunTheJobDirectly()
    {
        // Arrange
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
    public async Task ItShouldRunTheJobInTheBackground()
    {
        // Arrange
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
    public async Task ItShouldRunManyParallelJobsInTheBackground()
    {
        // Arrange
        var jobList = new List<Guid>();
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        
        for (var idx = 0; idx < 100; idx++)
        {
            var job = jobManager.NewJob<SimpleJobWithDelayTest>();
            var jobId = await jobManager.ScheduleJobAsync(job);
            jobList.Add(jobId);
        }

        // NOTE: moving this to before the job scheduling seems to trigger a rather hefty lock convoy on the db level
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
    [TestCase(true, 1)]
    [TestCase(true, 3)]
    [TestCase(true, 30)]
    [TestCase(false, 1)]
    [TestCase(false, 3)]
    [TestCase(false, 30)]
    public async Task ItShouldRunAndFailAndSucceedDirectly(bool failUsingException, int succeedAtRun)
    {
        // Arrange
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
    [TestCase(true, 1)]
    [TestCase(true, 2)]
    [TestCase(true, 3)]
    [TestCase(true, 10)]
    [TestCase(false, 1)]
    [TestCase(false, 2)]
    [TestCase(false, 3)]
    [TestCase(false, 10)]
    public async Task ItShouldRunAndFailAndSucceedInTheBackground(bool failUsingException, int succeedAtRun)
    {
        // Arrange
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
    [TestCase(true, 1)]
    [TestCase(true, 3)]
    [TestCase(true, 30)]
    [TestCase(false, 1)]
    [TestCase(false, 3)]
    [TestCase(false, 30)]
    public async Task ItShouldRunAndFailAndGiveUpDirectly(bool failUsingException, int maxAttempts)
    {
        // Arrange
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
    [TestCase(true, 1)]
    [TestCase(true, 3)]
    [TestCase(true, 10)]
    [TestCase(false, 1)]
    [TestCase(false, 3)]
    [TestCase(false, 10)]
    public async Task ItShouldRunAndFailAndGiveUpInTheBackground(bool failUsingException, int maxAttempts)
    {
        // Arrange
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
    public async Task ItShouldRunAndAbortDirectly()
    {
        // Arrange
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
    public async Task ItShouldRunAndAbortInTheBackground()
    {
        // Arrange
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
    public async Task ItShouldRescheduleDirectly()
    {
        // Arrange
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
    public async Task ItShouldRescheduleInTheBackground()
    {
        // Arrange
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
    [TestCase(true)]
    [TestCase(false)]
    public async Task ItShouldRescheduleOnOperationCancelledDirectly(bool cancelUsingException)
    {
        // Arrange
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
    [TestCase(true)]
    [TestCase(false)]
    public async Task ItShouldRescheduleOnOperationCancelledInTheBackground(bool cancelUsingException)
    {
        // Arrange
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
    public async Task ItShouldScheduleUniqueJob()
    {
        // Arrange
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();

        var job1 = _host!.Services.GetRequiredService<JobWithHash>();
        var jobId1 = await jobManager.ScheduleJobAsync(job1);

        // Act
        var job2 = _host!.Services.GetRequiredService<JobWithHash>();
        var jobId2 = await jobManager.ScheduleJobAsync(job2);

        Assert.That(jobId1, Is.EqualTo(jobId2));

        AssertLogEvents();
    }

    //

    [Test]
    [TestCase(0)]
    [TestCase(100)]
    public async Task ItShouldDeleteExpiredSuccessfulJobsDirectly(int deleteAfterMilliseconds)
    {
        // Arrange
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
    [TestCase(0)]
    [TestCase(100)]
    public async Task ItShouldDeleteExpiredSuccessfulJobsInTheBackground(int deleteAfterMilliseconds)
    {
        // Arrange
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
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
        var jobCleanUpBackgroundService = _host!.Services.GetRequiredService<JobCleanUpBackgroundService>();
        jobCleanUpBackgroundService.PulseBackgroundProcessor();

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
    [TestCase(0)]
    [TestCase(100)]
    public async Task ItShouldDeleteExpiredUnsuccessfulJobsDirectly(int deleteAfterMilliseconds)
    {
        // Arrange
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
    [TestCase(0)]
    [TestCase(100)]
    public async Task ItShouldDeleteExpiredUnsuccessfulJobsInTheBackground(int deleteAfterMilliseconds)
    {
        // Arrange
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
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
        var jobCleanUpBackgroundService = _host!.Services.GetRequiredService<JobCleanUpBackgroundService>();
        jobCleanUpBackgroundService.PulseBackgroundProcessor();

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
    public async Task ItShouldRunAChainedJobDirectly()
    {
        // Arrange
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
    public async Task ItShouldRunAChainedJobInTheBackground()
    {
        // Arrange
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
    
    
    private static async Task WaitForJobStatus<T>(IJobManager jobManager, Guid jobId, JobState status, TimeSpan maxWaitTime) where T : AbstractJob
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var job = await jobManager.GetJobAsync<T>(jobId) ?? throw new Exception("Test job not found");
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
