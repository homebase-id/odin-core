using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class JobWithHashData
{
    public string SomeJobData { get; set; } = "uninitialized";    
}

public class JobWithHash(ILogger<JobWithHash> logger) : AbstractJob
{
    public JobWithHashData JobData { get; private set; } = new ();
    
    //
    
    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running JobWithHash");
        JobData.SomeJobData = "hurrah!";
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
        JobData = OdinSystemSerializer.DeserializeOrThrow<JobWithHashData>(json);
    }

    //

    public override string? CreateJobHash()
    {
        var text = JobType + SerializeJobData();
        return SHA256.HashData(text.ToUtf8ByteArray()).ToBase64();
    }
}



