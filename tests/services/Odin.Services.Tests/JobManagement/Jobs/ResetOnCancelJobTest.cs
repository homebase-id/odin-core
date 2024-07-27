using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class ResetOnCancelJobTestData
{
    public bool CancelUsingException { get; set; }
}

public class ResetOnCancelJobTest(ILogger<ResetOnCancelJobTest> logger) : AbstractJob
{
    public ResetOnCancelJobTestData JobData { get; set; } = new ();

    //
    
    public override Task<RunResult> Run(CancellationToken cancellationToken)
    {
        // Don't do this in production code. This is for testing purposes only.
        if (cancellationToken == CancellationToken.None)
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            cancellationToken = cts.Token;
        }

        logger.LogInformation("Running ResetOnCancelJobTest");

        if (!cancellationToken.IsCancellationRequested)
        {
            throw new Exception("Unexpected. Fix your test!");
        }

        if (JobData.CancelUsingException)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return Task.FromResult(RunResult.Reset);
    }
    
    //

    public override string? SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(JobData);
    }
    
    //

    public override void DeserializeJobData(string json)
    {
        JobData = OdinSystemSerializer.DeserializeOrThrow<ResetOnCancelJobTestData>(json);
    }

    //
}



