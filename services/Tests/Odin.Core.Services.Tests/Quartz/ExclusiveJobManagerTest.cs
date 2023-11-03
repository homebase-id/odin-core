using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Odin.Core.Services.Quartz;
using Odin.Test.Helpers.Logging;
using Odin.Test.Helpers.Quarz;
using Quartz;

namespace Odin.Core.Services.Tests.Quartz;

public class ExclusiveJobManagerTest
{
    [Test]
    public async Task ItShouldAddAnExlusiveJob()
    {
        var jobManager = new ExclusiveJobManager(TestLogFactory.CreateConsoleLogger<ExclusiveJobManager>());
        var job = new ExclusiveJobSimulation();

        var jobKey = new JobKey("name", "group");
        var triggerMock = new Mock<ITrigger>();
        var jobDetailMock = new Mock<IJobDetail>();
        jobDetailMock.Setup(m => m.Key).Returns(jobKey);
        var jobExecutionContext = new Mock<IJobExecutionContext>();
        jobExecutionContext.Setup(m => m.JobInstance).Returns(job);
        jobExecutionContext.Setup(m => m.JobDetail).Returns(jobDetailMock.Object);

        var jobVetoed = await jobManager.VetoJobExecution(triggerMock.Object, jobExecutionContext.Object, default);
        Assert.That(jobVetoed, Is.False);
        Assert.That(jobManager.Exists(jobKey), Is.True);
        Assert.That(jobManager.Exists(jobKey.ToString()), Is.True);
        Assert.That(jobManager.Exists("somethingelse"), Is.False);
    }

    //

    [Test]
    public async Task ItShouldNotAddADuplicateExlusiveJobUnlessRemoved()
    {
        var jobManager = new ExclusiveJobManager(TestLogFactory.CreateConsoleLogger<ExclusiveJobManager>());
        var job = new ExclusiveJobSimulation();

        var jobKey = new JobKey("name", "group");
        var triggerMock = new Mock<ITrigger>();
        var jobDetailMock = new Mock<IJobDetail>();
        jobDetailMock.Setup(m => m.Key).Returns(jobKey);
        var jobExecutionContext = new Mock<IJobExecutionContext>();
        jobExecutionContext.Setup(m => m.JobInstance).Returns(job);
        jobExecutionContext.Setup(m => m.JobDetail).Returns(jobDetailMock.Object);

        var jobVetoed = await jobManager.VetoJobExecution(triggerMock.Object, jobExecutionContext.Object, default);
        Assert.That(jobVetoed, Is.False);

        jobVetoed = await jobManager.VetoJobExecution(triggerMock.Object, jobExecutionContext.Object, default);
        Assert.That(jobVetoed, Is.True);

        jobManager.RemoveJob(jobKey);

        jobVetoed = await jobManager.VetoJobExecution(triggerMock.Object, jobExecutionContext.Object, default);
        Assert.That(jobVetoed, Is.False);
    }

    //

    [Test]
    public async Task ItShouldIgnoreNonExlusiveJobs()
    {
        var jobManager = new ExclusiveJobManager(TestLogFactory.CreateConsoleLogger<ExclusiveJobManager>());
        var job = new NonExclusiveJobSimulation();

        var jobKey = new JobKey("name", "group");
        var triggerMock = new Mock<ITrigger>();
        var jobDetailMock = new Mock<IJobDetail>();
        jobDetailMock.Setup(m => m.Key).Returns(jobKey);
        var jobExecutionContext = new Mock<IJobExecutionContext>();
        jobExecutionContext.Setup(m => m.JobInstance).Returns(job);
        jobExecutionContext.Setup(m => m.JobDetail).Returns(jobDetailMock.Object);

        var jobVetoed = await jobManager.VetoJobExecution(triggerMock.Object, jobExecutionContext.Object, default);
        Assert.That(jobVetoed, Is.False);
        jobVetoed = await jobManager.VetoJobExecution(triggerMock.Object, jobExecutionContext.Object, default);
        Assert.That(jobVetoed, Is.False);

        Assert.That(jobManager.Exists(jobKey), Is.False);
        Assert.That(jobManager.Exists(jobKey.ToString()), Is.False);
        Assert.That(jobManager.Exists("somethingelse"), Is.False);
    }


}