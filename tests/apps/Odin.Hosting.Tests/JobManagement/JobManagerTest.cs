using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Logging.CorrelationId;
using Odin.Hosting.JobManagement;
using Odin.Hosting.Tests.JobManagement.Jobs;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Test.Helpers.Logging;
using Quartz;

namespace Odin.Hosting.Tests.JobManagement;

[Timeout(60000)]
public class JobManagerTest
{
    private readonly ILogger<JobManagerTest> _logger = TestLogFactory.CreateConsoleLogger<JobManagerTest>();
    private readonly TimeSpan _maxWaitForJobStatus = TimeSpan.FromSeconds(5);
    private string _tempPath;
    private IHost _host;

    [SetUp]
    public void Setup()
    {
        _logger.LogDebug("JobManagerTest.Setup() enter");
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);
        _logger.LogDebug("JobManagerTest.Setup() exit");
    }

    //

    [TearDown]
    public void TearDown()
    {
        _logger.LogDebug("JobManagerTest.TearDown() enter");
        // Tearing down too soon after schedulers having been created seems to randomly hang the test runner process.
        // Let's give it some time to do its thing.
        Task.Delay(200).Wait();
        _host.Dispose();
        _host = null;
        Directory.Delete(_tempPath, true);
        _logger.LogDebug("JobManagerTest.TearDown() exit");
    }

    //

    private IJobManager CreateHostedJobManager(bool initializeJobManager, int maxSchedulerConcurrency)
    {
        if (_host != null)
        {
            // This can happen if the previous test was cancelled by nunit for being too slow
            throw new Exception("NO!");
        }

        var config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                SystemDataRootPath = _tempPath
            },
            Job = new OdinConfiguration.JobSection
            {
                ConnectionPooling = false,
                MaxSchedulerConcurrency = maxSchedulerConcurrency
            },
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton(TestLogFactory.CreateLoggerFactory());
                services.AddSingleton<ICorrelationIdGenerator, CorrelationUniqueIdGenerator>();
                services.AddSingleton<ICorrelationContext, CorrelationContext>();
                services.AddJobManagementServices(config);

                services.AddTransient<NonExclusiveTestSchedule>();
                services.AddTransient<ExclusiveTestSchedule>();
                services.AddTransient<ChainTestSchedule>();
                services.AddTransient<EventDemoSchedule>();

                services.AddSingleton<EventDemoTestContainer>();
            })
            .Build();

        var jobManager = _host.Services.GetRequiredService<IJobManager>();
        if (initializeJobManager)
        {
            _logger.LogDebug("JobManagerTest.Initialize() before");
            jobManager.Initialize().Wait();
            _logger.LogDebug("JobManagerTest.Initialize() after");
        }

        return jobManager;
    }

    //

    private async Task WaitForJobStatus(IJobManager jobManager, JobKey jobKey, JobStatus status, TimeSpan maxWaitTime)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            var response = await jobManager.GetResponse(jobKey);
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
    public async Task ItShouldCreateSchedulersDuringInitialize()
    {
        var jobManager = CreateHostedJobManager(false, 10);
        await jobManager.Initialize();

        var exception = Assert.ThrowsAsync<JobManagerException>(async () =>
        {
            await jobManager.Initialize();
        });

        Assert.That(exception?.Message, Does.StartWith("Scheduler already exists:"));
    }

    //

    [Test]
    public async Task ItShouldCreateSchedulersAndStartPreconfiguredJob()
    {
        var jobKey = new JobKey("foo");

        var jobManager = CreateHostedJobManager(false, 10);
        await jobManager.Initialize(async () =>
        {
            var scheduler = _host.Services.GetRequiredService<NonExclusiveTestSchedule>();
            jobKey = await jobManager.Schedule<NonExclusiveTestJob>(scheduler);

            // Check if job exists
            var exists = await jobManager.Exists(jobKey);
            Assert.That(exists, Is.True);

            await Task.Delay(100);

            // Check that job does not start before we exit the initialize method
            var response = await jobManager.GetResponse(jobKey);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Scheduled));
        });

        // Wait for job to complete
        await WaitForJobStatus(jobManager, jobKey, JobStatus.Completed, _maxWaitForJobStatus);
    }

    [Test]
    public async Task ItShouldScheduleAndRunNonExlusiveJob()
    {
        var jobManager = CreateHostedJobManager(true, 10);

        var scheduler = _host.Services.GetRequiredService<NonExclusiveTestSchedule>();
        scheduler.TestEcho = "Hello World";
        var jobKey = await jobManager.Schedule<NonExclusiveTestJob>(scheduler);

        // NonExclusiveTestScheduler is using unique jobid
        var keyParts = jobKey.ToString().Split('.');
        Assert.That(keyParts[0], Has.Length.EqualTo(32));
        Assert.That(keyParts[1], Has.Length.GreaterThan(32));
        Assert.That(keyParts[1], Does.EndWith($"|{scheduler.SchedulerGroup}"));

        // Check if schedule exists
        var exists = await jobManager.Exists(jobKey);
        Assert.That(exists, Is.True);

        // Wait for job to complete
        await WaitForJobStatus(jobManager, jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        // Check response and data
        var (response, data) = await jobManager.GetResponse<NonExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
        Assert.That(data?.Echo, Is.EqualTo(scheduler.TestEcho));

        // Manually delete all traces of the job
        await jobManager.Delete(jobKey);
        exists = await jobManager.Exists(jobKey);
        Assert.That(exists, Is.False);
    }

    //

    [Test]
    public async Task ItShouldScheduleAndRunParallelNonExlusiveJob()
    {
        var jobManager = CreateHostedJobManager(true, 10);

        var scheduler1 = _host.Services.GetRequiredService<NonExclusiveTestSchedule>();
        scheduler1.TestEcho = "Hello World 1";
        var scheduler2 = _host.Services.GetRequiredService<NonExclusiveTestSchedule>();
        scheduler2.TestEcho = "Hello World 2";

        var jobKey1 = await jobManager.Schedule<NonExclusiveTestJob>(scheduler1);
        var jobKey2 = await jobManager.Schedule<NonExclusiveTestJob>(scheduler2);

        // Make sure the jobkeys are different, i.e two different jobs are scheduled
        Assert.That(jobKey1, Is.Not.EqualTo(jobKey2));

        // Wait for jobs to complete
        foreach (var jobKey in new[] { jobKey1, jobKey2 })
        {
            await WaitForJobStatus(jobManager, jobKey, JobStatus.Completed, _maxWaitForJobStatus);
        }

        // Check response and data from job 1
        {
            var (response, data) = await jobManager.GetResponse<NonExclusiveTestData>(jobKey1);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.Echo, Is.EqualTo(scheduler1.TestEcho));
        }

        // Check response and data from job 2
        {
            var (response, data) = await jobManager.GetResponse<NonExclusiveTestData>(jobKey2);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.Echo, Is.EqualTo(scheduler2.TestEcho));
        }
    }

    //

    [Test]
    public async Task ItShouldScheduleAndRunExlusiveJob()
    {
        var jobManager = CreateHostedJobManager(true, 10);

        var scheduler1 = _host.Services.GetRequiredService<ExclusiveTestSchedule>();
        scheduler1.TestEcho = "Hello World 1";
        var scheduler2 = _host.Services.GetRequiredService<ExclusiveTestSchedule>();
        scheduler2.TestEcho = "Hello World 2";

        // Verify that the jobtype is the same for both schedulers
        Assert.That(scheduler1.SchedulingKey, Is.EqualTo(scheduler2.SchedulingKey));

        var jobKey1 = await jobManager.Schedule<ExclusiveTestJob>(scheduler1);
        var jobKey2 = await jobManager.Schedule<ExclusiveTestJob>(scheduler2);

        // Make sure the jobkeys are the same, i.e only the first job is scheduled
        Assert.That(jobKey2, Is.EqualTo(jobKey1));

        var keyParts = jobKey1.ToString().Split('.');
        Assert.That(keyParts[0], Is.EqualTo(scheduler1.SchedulingKey));
        Assert.That(keyParts[1], Has.Length.GreaterThan(32));
        Assert.That(keyParts[1], Does.EndWith($"|{scheduler1.SchedulerGroup}"));

        // Wait for job to complete
        await WaitForJobStatus(jobManager, jobKey1, JobStatus.Completed, _maxWaitForJobStatus);

        // Check response and data from job 1
        {
            var (response, data) = await jobManager.GetResponse<ExclusiveTestData>(jobKey1);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.Echo, Is.EqualTo(scheduler1.TestEcho));
        }
    }

    //

    [Test]
    public async Task ItShouldRunAndFail()
    {
        var jobManager = CreateHostedJobManager(true, 10);

        var scheduler = _host.Services.GetRequiredService<ExclusiveTestSchedule>();
        scheduler.TestEcho = "Hello World 1";
        scheduler.FailCount = 1;
        scheduler.RetryCount = 0;

        var jobKey = await jobManager.Schedule<ExclusiveTestJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobManager, jobKey, JobStatus.Failed, _maxWaitForJobStatus);

        // Check response and data from job
        var (response, data) = await jobManager.GetResponse<ExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.Failed));
        Assert.That(data, Is.Null);
    }

    //

    [Test]
    public async Task ItShouldRunAndFailAndRetryAndSucceed()
    {
        var jobManager = CreateHostedJobManager(true, 10);

        var scheduler = _host.Services.GetRequiredService<ExclusiveTestSchedule>();
        scheduler.TestEcho = "Hello World 1";
        scheduler.FailCount = 3;
        scheduler.RetryCount = 3;

        var jobKey = await jobManager.Schedule<ExclusiveTestJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobManager, jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        // Check response and data from job
        var (response, data) = await jobManager.GetResponse<ExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
        Assert.That(data?.Echo, Is.EqualTo(scheduler.TestEcho));
    }

    //

    [Test]
    public async Task ItShouldRunAndFailAndRetryAndFail()
    {
        var jobManager = CreateHostedJobManager(true, 10);

        var scheduler = _host.Services.GetRequiredService<ExclusiveTestSchedule>();
        scheduler.TestEcho = "Hello World 1";
        scheduler.FailCount = 2;
        scheduler.RetryCount = 1;

        var jobKey = await jobManager.Schedule<ExclusiveTestJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobManager, jobKey, JobStatus.Failed, _maxWaitForJobStatus);

        // Check response and data from job
        var (response, data) = await jobManager.GetResponse<ExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.Failed));
        Assert.That(data, Is.Null);
    }

    //

    [Test]
    public async Task ItShouldNotFindJob()
    {
        var jobManager = CreateHostedJobManager(true, 10);

        var jobKey = new JobKey(Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        var exists = await jobManager.Exists(jobKey);
        Assert.That(exists, Is.False);

        var (response, data) = await jobManager.GetResponse<ExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.NotFound));
        Assert.That(data, Is.Null);
    }

    //

    [Test]
    public async Task ItShouldBeRetainedWithRetention()
    {
        var jobManager = CreateHostedJobManager(true, 10);

        var scheduler = _host.Services.GetRequiredService<ExclusiveTestSchedule>();
        scheduler.TestEcho = "Hello World 1";
        scheduler.Retention = TimeSpan.FromSeconds(10);

        var jobKey = await jobManager.Schedule<ExclusiveTestJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobManager, jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        // Check response and data from job
        var (response, data) = await jobManager.GetResponse<ExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
        Assert.That(data?.Echo, Is.EqualTo(scheduler.TestEcho));
    }

    [Test]
    public async Task ItShouldBeDeletedWithoutRetention()
    {
        var jobManager = CreateHostedJobManager(true, 10);

        var scheduler = _host.Services.GetRequiredService<ExclusiveTestSchedule>();
        scheduler.TestEcho = "Hello World 1";
        scheduler.Retention = TimeSpan.FromSeconds(0);

        var jobKey = await jobManager.Schedule<ExclusiveTestJob>(scheduler);

        // Wait for job to complete and be removed
        await WaitForJobStatus(jobManager, jobKey, JobStatus.NotFound, _maxWaitForJobStatus);

        // Check response and data from job
        var (response, data) = await jobManager.GetResponse<ExclusiveTestData>(jobKey);
        Assert.That(response.Status, Is.EqualTo(JobStatus.NotFound));
        Assert.That(data, Is.Null);
    }

    [Test]
    public async Task ItShouldBeDeletedAfterRetentionPeriod()
    {
        var jobManager = CreateHostedJobManager(true, 10);

        var scheduler = _host.Services.GetRequiredService<ExclusiveTestSchedule>();
        scheduler.TestEcho = "Hello World 1";
        scheduler.Retention = TimeSpan.FromSeconds(2);

        var jobKey = await jobManager.Schedule<ExclusiveTestJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobManager, jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        // Check response and data from job
        {
            var (response, data) = await jobManager.GetResponse<ExclusiveTestData>(jobKey);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.Echo, Is.EqualTo(scheduler.TestEcho));
        }

        // Wait for job to be removed
        await WaitForJobStatus(jobManager, jobKey, JobStatus.NotFound, _maxWaitForJobStatus);

        // Check response and data from job
        {
            var (response, data) = await jobManager.GetResponse<ExclusiveTestData>(jobKey);
            Assert.That(response.Status, Is.EqualTo(JobStatus.NotFound));
            Assert.That(data, Is.Null);
        }
    }

    [Test]
    public async Task ItShouldChainJobs()
    {
        var jobManager = CreateHostedJobManager(true, 10);

        var scheduler = _host.Services.GetRequiredService<ChainTestSchedule>();
        scheduler.IterationCount = 3;

        var jobKey = await jobManager.Schedule<ChainTestJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobManager, jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        {
            var (response, data) = await jobManager.GetResponse<ChainTestData>(jobKey);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.IterationCount, Is.EqualTo(3));
            Assert.That(data.NextJobKey, Is.Not.EqualTo(""));
            jobKey = Helpers.ParseJobKey(data.NextJobKey);
        }

        await WaitForJobStatus(jobManager, jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        {
            var (response, data) = await jobManager.GetResponse<ChainTestData>(jobKey);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.IterationCount, Is.EqualTo(2));
            Assert.That(data.NextJobKey, Is.Not.EqualTo(""));
            jobKey = Helpers.ParseJobKey(data.NextJobKey);
        }

        await WaitForJobStatus(jobManager, jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        {
            var (response, data) = await jobManager.GetResponse<ChainTestData>(jobKey);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.IterationCount, Is.EqualTo(1));
            Assert.That(data.NextJobKey, Is.EqualTo(""));
        }
    }

    [Test]
    public async Task ItShouldExecuteEventOnJobStartingAndOnJobCompleting()
    {
        var jobManager = CreateHostedJobManager(true, 10);

        var eventDemoTestContainer = _host.Services.GetRequiredService<EventDemoTestContainer>();
        Assert.That(eventDemoTestContainer.Status, Has.Count.EqualTo(0));

        var scheduler = _host.Services.GetRequiredService<EventDemoSchedule>();
        scheduler.ShouldFail = false;

        var jobKey = await jobManager.Schedule<EventDemoJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobManager, jobKey, JobStatus.Completed, _maxWaitForJobStatus);

        // Give the event some time to sync and execute
        await Task.Delay(1000);

        Assert.That(eventDemoTestContainer.Status, Has.Count.EqualTo(2));
        Assert.That(eventDemoTestContainer.Status[0], Is.EqualTo(JobStatus.Started));
        Assert.That(eventDemoTestContainer.Status[1], Is.EqualTo(JobStatus.Completed));
    }

    [Test]
    public async Task ItShouldExecuteEventOnJobStartingAndOnJobFailed()
    {
        var jobManager = CreateHostedJobManager(true, 10);

        var eventDemoTestContainer = _host.Services.GetRequiredService<EventDemoTestContainer>();
        Assert.That(eventDemoTestContainer.Status, Has.Count.EqualTo(0));

        var scheduler = _host.Services.GetRequiredService<EventDemoSchedule>();
        scheduler.ShouldFail = true;

        var jobKey = await jobManager.Schedule<EventDemoJob>(scheduler);

        // Wait for job to complete
        await WaitForJobStatus(jobManager, jobKey, JobStatus.Failed, _maxWaitForJobStatus);

        // Give the event some time to sync and execute
        await Task.Delay(1000);

        Assert.That(eventDemoTestContainer.Status, Has.Count.EqualTo(2));
        Assert.That(eventDemoTestContainer.Status[0], Is.EqualTo(JobStatus.Started));
        Assert.That(eventDemoTestContainer.Status[1], Is.EqualTo(JobStatus.Failed));
    }

    [Test]
    public async Task SingleSchedulerShouldQueueOnThreadLimit()
    {
        var jobManager = CreateHostedJobManager(true, 1);
        var logger = _host.Services.GetRequiredService<ILogger<SleepyTestSchedule>>();

        var scheduler = new SleepyTestSchedule(logger, SchedulerGroup.Default)
        {
            TestEcho = "Hello World",
            SleepTime = 1000
        };

        var sw = Stopwatch.StartNew();
        var jobKeys = new List<JobKey>
        {
            await jobManager.Schedule<SleepyTestJob>(scheduler),
            await jobManager.Schedule<SleepyTestJob>(scheduler),
            await jobManager.Schedule<SleepyTestJob>(scheduler)
        };

        // Wait for jobs to complete
        foreach (var jobKey in jobKeys)
        {
            await WaitForJobStatus(jobManager, jobKey, JobStatus.Completed, _maxWaitForJobStatus);
        }

        // Jobs have run serially and thus time taken should be > 3s

        Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(3000));
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(5000));

        // Check response and data
        foreach (var jobKey in jobKeys)
        {
            var (response, data) = await jobManager.GetResponse<SleepyTestData>(jobKey);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.Echo, Is.EqualTo(scheduler.TestEcho));
        }

        // Manually delete all traces of the job
        foreach (var jobKey in jobKeys)
        {
            await jobManager.Delete(jobKey);
            var exists = await jobManager.Exists(jobKey);
            Assert.That(exists, Is.False);
        }
    }

    [Test]
    public async Task MultipleSchedulersShouldRunInParallelOnThreadLimit()
    {
        var jobManager = CreateHostedJobManager(true, 1);
        var logger = _host.Services.GetRequiredService<ILogger<SleepyTestSchedule>>();

        var scheduler1 = new SleepyTestSchedule(logger, SchedulerGroup.Default)
        {
            TestEcho = "Hello World",
            SleepTime = 1000
        };

        var scheduler2 = new SleepyTestSchedule(logger, SchedulerGroup.SlowLowPriority)
        {
            TestEcho = "Hello World",
            SleepTime = 1000
        };

        var scheduler3 = new SleepyTestSchedule(logger, SchedulerGroup.FastLowPriority)
        {
            TestEcho = "Hello World",
            SleepTime = 1000
        };

        var sw = Stopwatch.StartNew();
        var jobKeys = new List<JobKey>
        {
            await jobManager.Schedule<SleepyTestJob>(scheduler1),
            await jobManager.Schedule<SleepyTestJob>(scheduler2),
            await jobManager.Schedule<SleepyTestJob>(scheduler3),
        };

        // Wait for jobs to complete
        foreach (var jobKey in jobKeys)
        {
            await WaitForJobStatus(jobManager, jobKey, JobStatus.Completed, _maxWaitForJobStatus);
        }

        // Jobs have run in parallel and thus time taken should be < 2s
        Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(1000));
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(3000));

        // Check response and data
        foreach (var jobKey in jobKeys)
        {
            var (response, data) = await jobManager.GetResponse<SleepyTestData>(jobKey);
            Assert.That(response.Status, Is.EqualTo(JobStatus.Completed));
            Assert.That(data?.Echo, Is.EqualTo("Hello World"));
        }

        // Manually delete all traces of the job
        foreach (var jobKey in jobKeys)
        {
            await jobManager.Delete(jobKey);
            var exists = await jobManager.Exists(jobKey);
            Assert.That(exists, Is.False);
        }
    }
}