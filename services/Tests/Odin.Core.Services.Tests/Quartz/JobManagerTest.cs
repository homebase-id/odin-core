using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Quartz;
using Odin.Test.Helpers.Logging;
using Quartz;

namespace Odin.Core.Services.Tests.Quartz;

public class JobManagerTest
{
    private IHost _host;
    private IJobManager _jobManager;

    // [SetUp]
    // public void Setup()
    // {
    //     // Setup your host with a similar method to your production environment
    //     _host = Host.CreateDefaultBuilder()
    //         .ConfigureServices((hostContext, services) =>
    //         {
    //             // Mimic your production setup here
    //             services.AddQuartzServices(new OdinConfiguration()); // Assuming OdinConfiguration is mockable or can be setup for tests
    //             // Add other necessary mocks and services
    //         })
    //         .Build();
    //
    //     // Resolve the IJobManager instance
    //     _jobManager = _host.Services.GetService<IJobManager>();
    // }


    [Test]
    public async Task ItShouldAddANonExlusiveJob()
    {
        // var collerationContextMock = new Mock<ICorrelationContext>();
        // var logger = TestLogFactory.CreateConsoleLogger<JobManager>();
        // var jobManager = new JobManager(TestLogFactory.CreateConsoleLogger<ExclusiveJobManager>());

        // var loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
        // var jobManager = app.ApplicationServices.GetRequiredService<IJobManager>();
        //
        // var jobSchedule = new NonExclusiveTestScheduler(loggerFactory.CreateLogger<NonExclusiveTestScheduler>());
        // var jobKey = await jobManager.Schedule<NonExclusiveTestJob>(jobSchedule);

        // var jobSchedule = new ExclusiveTestScheduler(loggerFactory.CreateLogger<ExclusiveTestScheduler>());
        // var jobKey = await jobManager.Schedule<ExclusiveTestJob>(jobSchedule);
        // jobKey = await jobManager.Schedule<ExclusiveTestJob>(jobSchedule);
        // jobKey = await jobManager.Schedule<ExclusiveTestJob>(jobSchedule);
    }

    [Test]
    public async Task ItShouldAddAnExlusiveJob()
    {
        Assert.Fail();
        // var jobManager = new ExclusiveJobManager(TestLogFactory.CreateConsoleLogger<ExclusiveJobManager>());
        // var job = new ExclusiveJobSimulation();
        //
        // var jobKey = new JobKey("name", "group");
        // var triggerMock = new Mock<ITrigger>();
        // var jobDetailMock = new Mock<IJobDetail>();
        // jobDetailMock.Setup(m => m.Key).Returns(jobKey);
        // var jobExecutionContext = new Mock<IJobExecutionContext>();
        // jobExecutionContext.Setup(m => m.JobInstance).Returns(job);
        // jobExecutionContext.Setup(m => m.JobDetail).Returns(jobDetailMock.Object);
        //
        // var jobVetoed = await jobManager.VetoJobExecution(triggerMock.Object, jobExecutionContext.Object, default);
        // Assert.That(jobVetoed, Is.False);
        // Assert.That(jobManager.Exists(jobKey), Is.True);
        // Assert.That(jobManager.Exists(jobKey.ToString()), Is.True);
        // Assert.That(jobManager.Exists("somethingelse"), Is.False);
    }

    //

    [Test]
    public async Task ItShouldNotAddADuplicateExlusiveJobUnlessRemoved()
    {
        Assert.Fail();
        // var jobManager = new ExclusiveJobManager(TestLogFactory.CreateConsoleLogger<ExclusiveJobManager>());
        // var job = new ExclusiveJobSimulation();
        //
        // var jobKey = new JobKey("name", "group");
        // var triggerMock = new Mock<ITrigger>();
        // var jobDetailMock = new Mock<IJobDetail>();
        // jobDetailMock.Setup(m => m.Key).Returns(jobKey);
        // var jobExecutionContext = new Mock<IJobExecutionContext>();
        // jobExecutionContext.Setup(m => m.JobInstance).Returns(job);
        // jobExecutionContext.Setup(m => m.JobDetail).Returns(jobDetailMock.Object);
        //
        // var jobVetoed = await jobManager.VetoJobExecution(triggerMock.Object, jobExecutionContext.Object, default);
        // Assert.That(jobVetoed, Is.False);
        //
        // jobVetoed = await jobManager.VetoJobExecution(triggerMock.Object, jobExecutionContext.Object, default);
        // Assert.That(jobVetoed, Is.True);
        //
        // jobManager.RemoveJob(jobKey);
        //
        // jobVetoed = await jobManager.VetoJobExecution(triggerMock.Object, jobExecutionContext.Object, default);
        // Assert.That(jobVetoed, Is.False);
    }

    //

    [Test]
    public async Task ItShouldIgnoreNonExlusiveJobs()
    {
        Assert.Fail();
        // var jobManager = new ExclusiveJobManager(TestLogFactory.CreateConsoleLogger<ExclusiveJobManager>());
        // var job = new NonExclusiveJobSimulation();
        //
        // var jobKey = new JobKey("name", "group");
        // var triggerMock = new Mock<ITrigger>();
        // var jobDetailMock = new Mock<IJobDetail>();
        // jobDetailMock.Setup(m => m.Key).Returns(jobKey);
        // var jobExecutionContext = new Mock<IJobExecutionContext>();
        // jobExecutionContext.Setup(m => m.JobInstance).Returns(job);
        // jobExecutionContext.Setup(m => m.JobDetail).Returns(jobDetailMock.Object);
        //
        // var jobVetoed = await jobManager.VetoJobExecution(triggerMock.Object, jobExecutionContext.Object, default);
        // Assert.That(jobVetoed, Is.False);
        // jobVetoed = await jobManager.VetoJobExecution(triggerMock.Object, jobExecutionContext.Object, default);
        // Assert.That(jobVetoed, Is.False);
        //
        // Assert.That(jobManager.Exists(jobKey), Is.False);
        // Assert.That(jobManager.Exists(jobKey.ToString()), Is.False);
        // Assert.That(jobManager.Exists("somethingelse"), Is.False);
    }


}