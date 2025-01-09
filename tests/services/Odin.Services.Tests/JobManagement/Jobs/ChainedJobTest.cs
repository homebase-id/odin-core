using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class ChainedJobTestData
{
    public Guid? SimpleJobId { get; set; }
}

public class ChainedJobTest(ILogger<ChainedJobTest> logger, IJobManager jobManager) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("ef3590bc-b4df-48dd-8385-2f8ee84fa505");
    public override string JobType => JobTypeId.ToString();

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



