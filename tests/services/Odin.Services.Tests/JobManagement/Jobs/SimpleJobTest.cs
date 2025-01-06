using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class SimpleJobTestData
{
    public string SomeJobData { get; set; } = "uninitialized";    
}

public class SimpleJobTest(ILogger<SimpleJobTest> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("d7fa00d3-0230-4ff2-ae2f-157eb5b6ca81");
    public override string JobType => JobTypeId.ToString();

    public SimpleJobTestData JobData { get; private set; } = new ();
    
    //
    
    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running SimpleJobTest");
        JobData.SomeJobData = "hurrah!";
        return Task.FromResult(JobExecutionResult.Success());
    }
    
    //

    public override string SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(JobData);
    }
    
    //

    public override void DeserializeJobData(string json)
    {
        JobData = OdinSystemSerializer.DeserializeOrThrow<SimpleJobTestData>(json);
    }

    //
}



