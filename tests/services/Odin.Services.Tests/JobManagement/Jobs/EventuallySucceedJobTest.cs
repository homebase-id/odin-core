using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class EventuallySucceedJobTestData
{
    public int RunCount { get; set; }
    public bool FailUsingException { get; set; }
    public int SucceedAfterRuns { get; set; } = 3;
}

public class EventuallySucceedJobTest(ILogger<EventuallySucceedJobTest> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("8c120758-0abd-4076-adf4-0d73b9a0fa48");
    public override string JobType => JobTypeId.ToString();

    public EventuallySucceedJobTestData JobData { get; set; } = new ();

    //

    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running EventuallySucceedJobTest");
        JobData.RunCount++;

        if (JobData.RunCount < JobData.SucceedAfterRuns)
        {
            if (JobData.FailUsingException)
            {
                throw new System.Exception("Fail with exception");
            }
            return Task.FromResult(JobExecutionResult.Fail());
        }

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
        JobData = OdinSystemSerializer.DeserializeOrThrow<EventuallySucceedJobTestData>(json);
    }

    //

}