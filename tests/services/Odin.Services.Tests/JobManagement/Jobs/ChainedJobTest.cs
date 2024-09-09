using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class ChainedJobTestData
{
    public Guid? SimpleJobId { get; set; }
}

public class ChainedJobTest(ILogger<ChainedJobTest> logger, IJobManager jobManager) : AbstractJob
{
    public ChainedJobTestData JobData { get; private set; } = new ();
    
    //
    
    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running ChainedJobTest");

        var simpleJob = jobManager.NewJob<SimpleJobTest>();

        var jobId = await jobManager.ScheduleJobAsync(simpleJob, JobSchedule.Now);
        JobData.SimpleJobId = jobId;
        
        return JobExecutionResult.Success();
    }
    
    //

    public override string SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(JobData);
    }
    
    //

    public override void DeserializeJobData(string json)
    {
        JobData = OdinSystemSerializer.DeserializeOrThrow<ChainedJobTestData>(json);
    }

    //
}



