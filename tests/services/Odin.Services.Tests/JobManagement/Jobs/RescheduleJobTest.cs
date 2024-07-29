using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.JobManagement;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class RescheduleJobTest(ILogger<RescheduleJobTest> logger) : AbstractJob
{

    //
    
    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running RescheduleJobTest");
        return Task.FromResult(JobExecutionResult.Reschedule(new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }
    
    //

    public override string? SerializeJobData()
    {
        return null;
    }
    
    //

    public override void DeserializeJobData(string json)
    {
    }

    //
}



