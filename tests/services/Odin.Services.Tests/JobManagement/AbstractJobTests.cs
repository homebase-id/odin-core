using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Storage.SQLite.ServerDatabase;
using Odin.Services.JobManagement;
using Odin.Services.Tests.JobManagement.Jobs;

namespace Odin.Services.Tests.JobManagement;

public class AbstractJobTests
{
    [Test]
    public async Task ItShouldCreateJobInstanceFromTypeAndSerializedDataAndRunIt()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<SimpleJobTest>>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(ILogger<SimpleJobTest>)))
            .Returns(loggerMock.Object);
        
        var jobType = typeof(SimpleJobTest).AssemblyQualifiedName;
        var jobData = OdinSystemSerializer.Serialize(new SimpleJobTestData());
        var record = new JobsRecord
        {
            jobType = jobType, 
            jobData = jobData
        };
        
        // Act
        var job = AbstractJob.CreateInstance(serviceProviderMock.Object, record);

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
