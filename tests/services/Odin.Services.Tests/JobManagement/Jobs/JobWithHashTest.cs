using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class JobWithHashData
{
    public string SomeJobData { get; set; } = "uninitialized";    
}

public class JobWithHashTest(ILogger<JobWithHashTest> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("5bc32c6b-54a0-4f4f-949b-76a657b0f11b");
    public override string JobType => JobTypeId.ToString();

    public JobWithHashData JobData { get; private set; } = new ();
    
    //
    
    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken);
        logger.LogInformation("Running JobWithHash");
        JobData.SomeJobData = "hurrah!";
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
        JobData = OdinSystemSerializer.DeserializeOrThrow<JobWithHashData>(json);
    }

    //

    public override string? CreateJobHash()
    {
        var text = JobType + SerializeJobData();
        return SHA256.HashData(text.ToUtf8ByteArray()).ToBase64();
    }
}




