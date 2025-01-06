using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class AbortingJobTest(ILogger<AbortingJobTest> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("d8524739-d2ea-454b-afdd-fea3aa14b561");
    public override string JobType => JobTypeId.ToString();

    //
    
    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running AbortingJobTest");
        return Task.FromResult(JobExecutionResult.Abort());
    }
    
    //

    public override string? SerializeJobData()
    {
        return null;
    }
    
    //

    public override void DeserializeJobData(string json)
    {
        // Do nothing
    }

    //
}



