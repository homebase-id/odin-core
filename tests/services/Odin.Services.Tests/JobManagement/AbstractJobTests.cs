using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.System.Table;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Tests.JobManagement.Jobs;

namespace Odin.Services.Tests.JobManagement;

public class AbstractJobTests
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
    public async Task ItShouldCreateJobInstanceFromTypeIdAndSerializedDataAndRunIt()
    {
        var jobType = SimpleJobTest.JobTypeId.ToString();
        var jobData = OdinSystemSerializer.Serialize(new SimpleJobTestData());
        var record = new JobsRecord
        {
            jobType = jobType,
            jobData = jobData
        };

        // Act
        using var job = AbstractJob.CreateInstance(_container, record);

        // Assert
        Assert.NotNull(job);
        Assert.NotNull(job.Record);
        Assert.AreEqual(record, job.Record);
        Assert.IsInstanceOf<SimpleJobTest>(job);
        var simpleJobTest = job as SimpleJobTest;
        Assert.NotNull(simpleJobTest);
        Assert.NotNull(simpleJobTest!.JobData);
        Assert.AreEqual("uninitialized", simpleJobTest.JobData.SomeJobData);

        await simpleJobTest.Run(CancellationToken.None);
        Assert.AreEqual("hurrah!", simpleJobTest.JobData.SomeJobData);
    }

    [Test]
    public async Task ItShouldCreateJobInstanceFromTypeAndSerializedDataAndRunIt()
    {
        var jobType = typeof(SimpleJobTest).AssemblyQualifiedName;
        var jobData = OdinSystemSerializer.Serialize(new SimpleJobTestData());
        var record = new JobsRecord
        {
            jobType = jobType, 
            jobData = jobData
        };
        
        // Act
        using var job = AbstractJob.CreateInstance(_container, record);

        // Assert
        Assert.NotNull(job);
        Assert.NotNull(job.Record);
        Assert.AreEqual(record, job.Record);
        Assert.IsInstanceOf<SimpleJobTest>(job);
        var simpleJobTest = job as SimpleJobTest;
        Assert.NotNull(simpleJobTest);
        Assert.NotNull(simpleJobTest!.JobData);
        Assert.AreEqual("uninitialized", simpleJobTest.JobData.SomeJobData);

        await simpleJobTest.Run(CancellationToken.None);
        Assert.AreEqual("hurrah!", simpleJobTest.JobData.SomeJobData);
    }

    [Test]
    public void ItShouldThrowOnUnknownJobTypeId()
    {
        var jobType = Guid.Parse("a919e97a-b1dc-4b8c-bf8e-fb3bcdb587de").ToString();
        var jobData = OdinSystemSerializer.Serialize(new SimpleJobTestData());
        var record = new JobsRecord
        {
            jobType = jobType,
            jobData = jobData
        };

        // Assert
        var exception = Assert.Throws<OdinSystemException>(() =>
        {
            // Act
            using var job = AbstractJob.CreateInstance(_container, record);
        });
        Assert.AreEqual($"Job type with ID {jobType} is not registered", exception!.Message);
    }

    [Test]
    public void ItShouldThrowOnUnknownJobType()
    {
        var jobType = "namespace.foo.bar.baz";
        var jobData = OdinSystemSerializer.Serialize(new SimpleJobTestData());
        var record = new JobsRecord
        {
            jobType = jobType,
            jobData = jobData
        };

        // Assert
        var exception = Assert.Throws<OdinSystemException>(() =>
        {
            // Act
            using var job = AbstractJob.CreateInstance(_container, record);
        });
        Assert.AreEqual($"Unable to find job type {jobType}", exception!.Message);
    }


}
