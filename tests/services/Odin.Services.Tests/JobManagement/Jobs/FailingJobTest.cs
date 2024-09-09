using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class FailingJobTestData
{
    public string SomeJobData { get; set; } = "uninitialized";
}

public class FailingJobTest(ILogger<FailingJobTest> logger) : AbstractJob
{
    public FailingJobTestData JobData { get; private set; } = new ();
    
    //
    
    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running FailingJobTest");
        JobData.SomeJobData = "hurrah!";
        throw new Exception("oh no!");
    }
    
    //

    public override string SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(JobData);
    }
    
    //

    public override void DeserializeJobData(string json)
    {
        JobData = OdinSystemSerializer.DeserializeOrThrow<FailingJobTestData>(json);
    }

    //
}



