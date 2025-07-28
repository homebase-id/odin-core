using System;
using Autofac;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.System;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Tests.JobManagement.Jobs;

namespace Odin.Services.Tests.JobManagement;

public class JobApiResponseTests
{
    private ILifetimeScope _container = null!;

    [SetUp]
    public void Setup()
    {
        var builder = new ContainerBuilder();
        var mockLogger = Mock.Of<ILogger<SimpleJobTest>>();
        builder.RegisterInstance(mockLogger).As<ILogger<SimpleJobTest>>();

        var jobTypeRegistry = new JobTypeRegistry();
        builder.RegisterInstance(jobTypeRegistry).As<IJobTypeRegistry>().SingleInstance();
        jobTypeRegistry.RegisterJobType<SimpleJobTest>(builder, SimpleJobTest.JobTypeId);

        _container = builder.Build();    
    }

    [Test]
    public void ItShouldDeserializeJobApiResponse()
    {
        var record = new JobsRecord
        {
            id = Guid.NewGuid(),
            state = (int)JobState.Scheduled,
            lastError = "some error",
            jobType = SimpleJobTest.JobTypeId.ToString(),
        };

        using var job = AbstractJob.CreateInstance<SimpleJobTest>(_container, record);
        job.JobData.SomeJobData = "hurrah!";

        Assert.That(job.Id, Is.EqualTo(record.id));
        Assert.That(job.State, Is.EqualTo((JobState)record.state));
        Assert.That(job.LastError, Is.EqualTo(record.lastError));
        Assert.That(job.JobType, Is.EqualTo(record.jobType));

        var apiResponseObject = job.CreateApiResponseObject();
        var json = OdinSystemSerializer.Serialize(apiResponseObject);

        var (clone, data) = JobApiResponse.Deserialize<SimpleJobTestData>(json);
        Assert.That(clone.JobId, Is.EqualTo(record.id));
        Assert.That(clone.State, Is.EqualTo((JobState)record.state));
        Assert.That(clone.Error, Is.EqualTo(record.lastError));
        Assert.That(data, Is.Not.Null);
        Assert.That(data!.SomeJobData, Is.EqualTo(job.JobData.SomeJobData));
    }
}