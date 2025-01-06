using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class RescheduleJobTest(ILogger<RescheduleJobTest> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("621134bc-d789-4ca2-a8a3-99688252ec40");
    public override string JobType => JobTypeId.ToString();

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



