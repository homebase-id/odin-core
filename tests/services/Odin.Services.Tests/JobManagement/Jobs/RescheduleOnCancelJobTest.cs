using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class RescheduleOnCancelJobTestData
{
    public bool CancelUsingException { get; set; }
}

public class RescheduleOnCancelJobTest(ILogger<RescheduleOnCancelJobTest> logger) : AbstractJob
{
    public RescheduleOnCancelJobTestData JobData { get; set; } = new ();

    //
    
    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        // Overwrite cancellationToken for testing purposes
        var cts = new CancellationTokenSource();
        cts.Cancel();
        cancellationToken = cts.Token;

        logger.LogInformation("Running RescheduleOnCancelJobTest");

        if (!cancellationToken.IsCancellationRequested)
        {
            throw new Exception("Unexpected. Fix your test!");
        }

        if (JobData.CancelUsingException)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        // We add 3 seconds for good measure, mostly to not confuse the test runner.
        return Task.FromResult(JobExecutionResult.Reschedule(DateTimeOffset.Now.AddSeconds(3)));
    }
    
    //

    public override string? SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(JobData);
    }
    
    //

    public override void DeserializeJobData(string json)
    {
        JobData = OdinSystemSerializer.DeserializeOrThrow<RescheduleOnCancelJobTestData>(json);
    }

    //
}



