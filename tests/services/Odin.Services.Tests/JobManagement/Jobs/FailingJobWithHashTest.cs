using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class FailingJobWithHashData
{
    public string SomeJobData { get; set; } = "uninitialized";    
}

public class FailingJobWithHashTest(ILogger<FailingJobWithHashTest> logger) : AbstractJob
{
    static readonly Random _random = new ();
    public FailingJobWithHashData JobData { get; private set; } = new ();
    
    //
    
    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        await Task.Delay(_random.Next(1, 30), cancellationToken);
        logger.LogInformation("Running FailingJobWithHashData");
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
        JobData = OdinSystemSerializer.DeserializeOrThrow<FailingJobWithHashData>(json);
    }

    //

    public override string? CreateJobHash()
    {
        var text = "full-throttle!";
        return SHA256.HashData(text.ToUtf8ByteArray()).ToBase64();
    }
}




