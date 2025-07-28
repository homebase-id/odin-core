using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.System;
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
        ClassicAssert.NotNull(job);
        ClassicAssert.NotNull(job.Record);
        ClassicAssert.AreEqual(record, job.Record);
        ClassicAssert.IsInstanceOf<SimpleJobTest>(job);
        var simpleJobTest = job as SimpleJobTest;
        ClassicAssert.NotNull(simpleJobTest);
        ClassicAssert.NotNull(simpleJobTest!.JobData);
        ClassicAssert.AreEqual("uninitialized", simpleJobTest.JobData.SomeJobData);

        await simpleJobTest.Run(CancellationToken.None);
        ClassicAssert.AreEqual("hurrah!", simpleJobTest.JobData.SomeJobData);
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
        ClassicAssert.NotNull(job);
        ClassicAssert.NotNull(job.Record);
        ClassicAssert.AreEqual(record, job.Record);
        ClassicAssert.IsInstanceOf<SimpleJobTest>(job);
        var simpleJobTest = job as SimpleJobTest;
        ClassicAssert.NotNull(simpleJobTest);
        ClassicAssert.NotNull(simpleJobTest!.JobData);
        ClassicAssert.AreEqual("uninitialized", simpleJobTest.JobData.SomeJobData);

        await simpleJobTest.Run(CancellationToken.None);
        ClassicAssert.AreEqual("hurrah!", simpleJobTest.JobData.SomeJobData);
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
        ClassicAssert.AreEqual($"Job type with ID {jobType} is not registered", exception!.Message);
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
        ClassicAssert.AreEqual($"Unable to find job type {jobType}", exception!.Message);
    }


}
