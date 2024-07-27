using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Logging.Hostname;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Tasks;
using Odin.Services.Background;
using Odin.Services.Background.Services.System;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Services.Tests.JobManagement.Jobs;
using Odin.Test.Helpers.Logging;

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
        };

        var host = Host.CreateDefaultBuilder()
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
                    provider.GetRequiredService<IServiceProvider>(),
                    "system"
                ));

                services.AddSingleton<JobJanitorBackgroundService>();
                services.AddSingleton<JobRunnerBackgroundService>();
                services.AddSingleton<IJobManager, JobManager>();
                
                services.AddTransient<SimpleJobTest>();
            })
            .Build();

        return host;
    }
    
    //
    
    
    //
    
    private void AssertLogEvents()
    {
        var logEvents = _host!.Services.GetRequiredService<ILogEventMemoryStore>().GetLogEvents();
        LogEvents.AssertEvents(logEvents);
    }
    
    //
    
    private async Task StartBackgroundServices()
    {
        await _backgroundServiceManager!.StartAsync(
            nameof(JobJanitorBackgroundService), 
            _host!.Services.GetRequiredService<JobJanitorBackgroundService>());
        
        await _backgroundServiceManager!.StartAsync(
            nameof(JobRunnerBackgroundService), 
            _host!.Services.GetRequiredService<JobRunnerBackgroundService>());
    }
    
    //

    private async Task StopBackgroundServices()
    {
        await _backgroundServiceManager!.StopAsync(nameof(JobRunnerBackgroundService));
        await _backgroundServiceManager!.StopAsync(nameof(JobJanitorBackgroundService));
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
        var job = _host!.Services.GetRequiredService<SimpleJobTest>();

        // Act
        var jobId = await jobManager.ScheduleJobAsync(job);
        
        // Assert
        Assert.That(jobId, Is.Not.EqualTo(Guid.Empty));

        jobCount = await jobManager.CountJobsAsync();
        Assert.That(jobCount, Is.EqualTo(1));

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
        
        var job = _host!.Services.GetRequiredService<SimpleJobTest>();
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
        
        var job = _host!.Services.GetRequiredService<SimpleJobTest>();
        var jobId = await jobManager.ScheduleJobAsync(job);
        Assert.That(jobId, Is.Not.EqualTo(Guid.Empty));
        
        // Act
        var scheduledJob = await jobManager.GetJobAsync<SimpleJobTest>(jobId);
        
        // Assert
        Assert.That(scheduledJob, Is.Not.Null);
        Assert.AreEqual(job.JobData.SomeSerializedData, scheduledJob!.JobData.SomeSerializedData);
        Assert.AreEqual("SimpleJobTest", scheduledJob!.Record!.name);
        
        AssertLogEvents();
    }
    
    //
    
    [Test]
    public async Task ItShouldRunTheJobDirectly()
    {
        // Arrange
        var jobManager = _host!.Services.GetRequiredService<IJobManager>();
        
        var job = _host!.Services.GetRequiredService<SimpleJobTest>();
        var jobId = await jobManager.ScheduleJobAsync(job);
        Assert.That(jobId, Is.Not.EqualTo(Guid.Empty));
        
        var scheduledJob = await jobManager.GetJobAsync<SimpleJobTest>(jobId);
        Assert.That(scheduledJob, Is.Not.Null);
        Assert.AreEqual(job.JobData.SomeSerializedData, scheduledJob!.JobData.SomeSerializedData);

        // Act
        await jobManager.ExecuteJobAsync(scheduledJob, CancellationToken.None);
        
        // Assert
        var completedJob = await jobManager.GetJobAsync<SimpleJobTest>(jobId);
        Assert.That(completedJob, Is.Not.Null);
        Assert.AreEqual("hurrah!", completedJob!.JobData.SomeSerializedData);
        
        AssertLogEvents();
    }
    
    //

    [Test]
    public async Task ItShouldRunAndFailAndSucceedDirectly()
    {
        Assert.Fail();

    }

    [Test]
    public async Task ItShouldRunAndFailAndGiveUpDirectly()
    {
        Assert.Fail();

    }
    
    [Test]
    public async Task ItShouldRunAndResetBeforeCompletion()
    {
        Assert.Fail();

    }
    
    [Test]
    public async Task ItShouldRunAndAbort()
    {
        Assert.Fail();

    }
  
    


    //
    
    [Test]
    public async Task STUFF_IN_BACKGROUND()
    {
        await StartBackgroundServices();
        
        await StopBackgroundServices();
        AssertLogEvents();
    }

    
    
}