using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class FailingJobTestData
{
    public string SomeJobData { get; set; } = "uninitialized";
}

public class FailingJobTest(ILogger<FailingJobTest> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("bb0d797a-e4d3-4aab-bf6d-4d04a2ba3215");
    public override string JobType => JobTypeId.ToString();

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



