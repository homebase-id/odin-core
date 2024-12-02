using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.System.Table;
using Odin.Services.JobManagement;
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
        builder.RegisterType<SimpleJobTest>().AsSelf().InstancePerDependency();
        _container = builder.Build();    
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
}
