using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Quartz;
using Odin.Hosting.Quartz;
using Odin.Hosting.Tests.Quartz.Jobs;
using Quartz;

namespace Odin.Hosting.Tests.Quartz;

public class JobManagerTest
{
    private readonly TimeSpan _maxWaitForJobStatus = TimeSpan.FromSeconds(5);
    private string _tempPath;
    private OdinConfiguration _config;
    private IHost _host;
    private IJobManager _jobManager;
    private IScheduler _scheduler;

    [SetUp]
    public void Setup()
    {
        var loggerMock = new Mock<ILogger>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(loggerMock.Object);

        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);

        _config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection()
            {
                SystemDataRootPath = _tempPath
            },
            Quartz = new OdinConfiguration.QuartzSection()
            {
                SqliteDatabaseFileName = "quartz.sqlite",
                MaxConcurrency = 256
            },
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton<ILoggerFactory>(loggerFactoryMock.Object);
                services.AddSingleton<ICorrelationIdGenerator, CorrelationUniqueIdGenerator>();
                services.AddSingleton<ICorrelationContext, CorrelationContext>();
                services.AddQuartzServices(_config);

                services.AddTransient<NonExclusiveTestScheduler>();
                services.AddTransient<ExclusiveTestScheduler>();
                services.AddTransient<ChainTestScheduler>();
                services.AddTransient<EventDemoScheduler>();

                services.AddSingleton<EventDemoTestContainer>();
            })
            .Build();

        _jobManager = _host.Services.GetRequiredService<IJobManager>();

        // Strange that we need to start the scheduler here. This is not reqired in the real application.
        var schedulerFactory = _host.Services.GetRequiredService<ISchedulerFactory>();
        _scheduler = schedulerFactory.GetScheduler().Result;
        _scheduler.Start().Wait();
    }

    //

    [TearDown]
    public void TearDown()
    {
        _scheduler.Shutdown().Wait();
        _host.Dispose();
        Directory.Delete(_tempPath, true);
    }

    //

    private async Task WaitForJobStatus(JobKey jobKey, JobStatus status, TimeSpan maxWaitTime)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var response = await _jobManager.GetResponse(jobKey);
            if (response.Status == status)
            {
                break;
            }
            if (sw.Elapsed > maxWaitTime)
            {
                throw new TimeoutException(
                    $"Job did not reach status {status} within {maxWaitTime}. Last status: {response.Status}");
            }
            await Task.Delay(100);
        }
    }

    //

    [Test]
    public async Task ItShouldScheduleAndRunNonExlusiveJob()
    {
        var scheduler = _host.Services.GetRequiredService<NonExclusiveTestScheduler>();
        scheduler.TestEcho = "Hello World";
        var jobKey = await _jobManager.Schedule<NonExclusiveTestJob>(scheduler);

        // NonExclusiveTestScheduler is using unique jobid, so we can expect the jobkey to be 2x64 characters
        var keyParts = jobKey.ToString().Split('.');
        Assert.That(keyParts[0], Has.Length.EqualTo(64));
        Assert.That(keyParts[1], Has.Length.EqualTo(64));

        // Check if schedule exists
        var exists = await _jobManager.Exists(jobKey);
        Assert.That(exists, Is.True);

        // Wait for job to complete
        await WaitForJobStatus(jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        // Check response and data
        var (response, data) = await _jobManager.GetResponse<NonExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
        Assert.That(data?.Echo, Is.EqualTo(scheduler.TestEcho));

        // Manually delete all traces of the job
        await _jobManager.Delete(jobKey);
        exists = await _jobManager.Exists(jobKey);
        Assert.That(exists, Is.False);
    }

    //

    [Test]
    public async Task ItShouldScheduleAndRunParallelNonExlusiveJob()
    {
        var scheduler1 = _host.Services.GetRequiredService<NonExclusiveTestScheduler>();
        scheduler1.TestEcho = "Hello World 1";
        var scheduler2 = _host.Services.GetRequiredService<NonExclusiveTestScheduler>();
        scheduler2.TestEcho = "Hello World 2";

        var jobKey1 = await _jobManager.Schedule<NonExclusiveTestJob>(scheduler1);
        var jobKey2 = await _jobManager.Schedule<NonExclusiveTestJob>(scheduler2);

        // Make sure the jobkeys are different, i.e two different jobs are scheduled
        Assert.That(jobKey1, Is.Not.EqualTo(jobKey2));

        // Wait for jobs to complete
        foreach (var jobKey in new[] { jobKey1, jobKey2 })
        {
            await WaitForJobStatus(jobKey, JobStatus.Completed, _maxWaitForJobStatus);
        }

        // Check response and data from job 1
        {
            var (response, data) = await _jobManager.GetResponse<NonExclusiveTestData>(jobKey1);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.Echo, Is.EqualTo(scheduler1.TestEcho));
        }

        // Check response and data from job 2
        {
            var (response, data) = await _jobManager.GetResponse<NonExclusiveTestData>(jobKey2);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.Echo, Is.EqualTo(scheduler2.TestEcho));
        }
    }

    //

    [Test]
    public async Task ItShouldScheduleAndRunExlusiveJob()
    {
        var scheduler1 = _host.Services.GetRequiredService<ExclusiveTestScheduler>();
        scheduler1.TestEcho = "Hello World 1";
        var scheduler2 = _host.Services.GetRequiredService<ExclusiveTestScheduler>();
        scheduler2.TestEcho = "Hello World 2";

        // Verify that the jobtype is the same for both schedulers
        Assert.That(scheduler1.SchedulingKey, Is.EqualTo(scheduler2.SchedulingKey));

        var jobKey1 = await _jobManager.Schedule<ExclusiveTestJob>(scheduler1);
        var jobKey2 = await _jobManager.Schedule<ExclusiveTestJob>(scheduler2);

        // Make sure the jobkeys are the same, i.e only the first job is scheduled
        Assert.That(jobKey2, Is.EqualTo(jobKey1));

        // Wait for job to complete
        await WaitForJobStatus(jobKey1, JobStatus.Completed, _maxWaitForJobStatus);

        // Check response and data from job 1
        {
            var (response, data) = await _jobManager.GetResponse<ExclusiveTestData>(jobKey1);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.Echo, Is.EqualTo(scheduler1.TestEcho));
        }
    }

    //

    [Test]
    public async Task ItShouldRunAndFail()
    {
        var scheduler = _host.Services.GetRequiredService<ExclusiveTestScheduler>();
        scheduler.TestEcho = "Hello World 1";
        scheduler.FailCount = 1;
        scheduler.RetryCount = 0;

        var jobKey = await _jobManager.Schedule<ExclusiveTestJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobKey, JobStatus.Failed, _maxWaitForJobStatus);

        // Check response and data from job
        var (response, data) = await _jobManager.GetResponse<ExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.Failed));
        Assert.That(data, Is.Null);
    }

    //

    [Test]
    public async Task ItShouldRunAndFailAndRetryAndSucceed()
    {
        var scheduler = _host.Services.GetRequiredService<ExclusiveTestScheduler>();
        scheduler.TestEcho = "Hello World 1";
        scheduler.FailCount = 3;
        scheduler.RetryCount = 3;

        var jobKey = await _jobManager.Schedule<ExclusiveTestJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        // Check response and data from job
        var (response, data) = await _jobManager.GetResponse<ExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
        Assert.That(data?.Echo, Is.EqualTo(scheduler.TestEcho));
    }

    //

    [Test]
    public async Task ItShouldRunAndFailAndRetryAndFail()
    {
        var scheduler = _host.Services.GetRequiredService<ExclusiveTestScheduler>();
        scheduler.TestEcho = "Hello World 1";
        scheduler.FailCount = 2;
        scheduler.RetryCount = 1;

        var jobKey = await _jobManager.Schedule<ExclusiveTestJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobKey, JobStatus.Failed, _maxWaitForJobStatus);

        // Check response and data from job
        var (response, data) = await _jobManager.GetResponse<ExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.Failed));
        Assert.That(data, Is.Null);
    }

    //

    [Test]
    public async Task ItShouldNotFindJob()
    {
        var jobKey = new JobKey(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        var exists = await _jobManager.Exists(jobKey);
        Assert.That(exists, Is.False);

        var (response, data) = await _jobManager.GetResponse<ExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.NotFound));
        Assert.That(data, Is.Null);
    }

    //

    [Test]
    public async Task ItShouldBeRetainedWithRetention()
    {
        var scheduler = _host.Services.GetRequiredService<ExclusiveTestScheduler>();
        scheduler.TestEcho = "Hello World 1";
        scheduler.Retention = TimeSpan.FromSeconds(10);

        var jobKey = await _jobManager.Schedule<ExclusiveTestJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        // Check response and data from job
        var (response, data) = await _jobManager.GetResponse<ExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
        Assert.That(data?.Echo, Is.EqualTo(scheduler.TestEcho));
    }

    [Test]
    public async Task ItShouldBeDeletedWithoutRetention()
    {
        var scheduler = _host.Services.GetRequiredService<ExclusiveTestScheduler>();
        scheduler.TestEcho = "Hello World 1";
        scheduler.Retention = TimeSpan.FromSeconds(0);

        var jobKey = await _jobManager.Schedule<ExclusiveTestJob>(scheduler);

        // Wait for job to complete and be removed
        await WaitForJobStatus(jobKey, JobStatus.NotFound, _maxWaitForJobStatus);

        // Check response and data from job
        var (response, data) = await _jobManager.GetResponse<ExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.NotFound));
        Assert.That(data, Is.Null);
    }

    [Test]
    public async Task ItShouldBeDeletedAfterRetentionPeriod()
    {
        var scheduler = _host.Services.GetRequiredService<ExclusiveTestScheduler>();
        scheduler.TestEcho = "Hello World 1";
        scheduler.Retention = TimeSpan.FromSeconds(2);

        var jobKey = await _jobManager.Schedule<ExclusiveTestJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        // Check response and data from job
        {
            var (response, data) = await _jobManager.GetResponse<ExclusiveTestData>(jobKey);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.Echo, Is.EqualTo(scheduler.TestEcho));
        }

        // Wait for job to be removed
        await WaitForJobStatus(jobKey, JobStatus.NotFound, _maxWaitForJobStatus);

        // Check response and data from job
        {
            var (response, data) = await _jobManager.GetResponse<ExclusiveTestData>(jobKey);
            Assert.That(response.Status, Is.EqualTo(JobStatus.NotFound));
            Assert.That(data, Is.Null);
        }
    }

    [Test]
    public async Task ItShouldChainJobs()
    {
        var scheduler = _host.Services.GetRequiredService<ChainTestScheduler>();
        scheduler.IterationCount = 3;

        var jobKey = await _jobManager.Schedule<ChainTestJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        {
            var (response, data) = await _jobManager.GetResponse<ChainTestData>(jobKey);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.IterationCount, Is.EqualTo(3));
            Assert.That(data.NextJobKey, Is.Not.EqualTo(""));
            jobKey = Helpers.ParseJobKey(data.NextJobKey);
        }

        await WaitForJobStatus(jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        {
            var (response, data) = await _jobManager.GetResponse<ChainTestData>(jobKey);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.IterationCount, Is.EqualTo(2));
            Assert.That(data.NextJobKey, Is.Not.EqualTo(""));
            jobKey = Helpers.ParseJobKey(data.NextJobKey);
        }

        await WaitForJobStatus(jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        {
            var (response, data) = await _jobManager.GetResponse<ChainTestData>(jobKey);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.IterationCount, Is.EqualTo(1));
            Assert.That(data.NextJobKey, Is.EqualTo(""));
        }
    }

    [Test]
    public async Task ItShouldExecuteEventOnJobStartingAndOnJobCompleting()
    {
        var eventDemoTestContainer = _host.Services.GetRequiredService<EventDemoTestContainer>();
        Assert.That(eventDemoTestContainer.Status, Has.Count.EqualTo(0));

        var scheduler = _host.Services.GetRequiredService<EventDemoScheduler>();
        scheduler.ShouldFail = false;

        var jobKey = await _jobManager.Schedule<EventDemoJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        // Give the event some time to sync and execute
        await Task.Delay(1000);

        Assert.That(eventDemoTestContainer.Status, Has.Count.EqualTo(2));
        Assert.That(eventDemoTestContainer.Status[0], Is.EqualTo(JobStatus.Started));
        Assert.That(eventDemoTestContainer.Status[1], Is.EqualTo(JobStatus.Completed));
    }

    [Test]
    public async Task ItShouldExecuteEventOnJobStartingAndOnJobFailed()
    {
        var eventDemoTestContainer = _host.Services.GetRequiredService<EventDemoTestContainer>();
        Assert.That(eventDemoTestContainer.Status, Has.Count.EqualTo(0));

        var scheduler = _host.Services.GetRequiredService<EventDemoScheduler>();
        scheduler.ShouldFail = true;

        var jobKey = await _jobManager.Schedule<EventDemoJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobKey, JobStatus.Failed, _maxWaitForJobStatus);

        // Give the event some time to sync and execute
        await Task.Delay(1000);

        Assert.That(eventDemoTestContainer.Status, Has.Count.EqualTo(2));
        Assert.That(eventDemoTestContainer.Status[0], Is.EqualTo(JobStatus.Started));
        Assert.That(eventDemoTestContainer.Status[1], Is.EqualTo(JobStatus.Failed));
    }
}