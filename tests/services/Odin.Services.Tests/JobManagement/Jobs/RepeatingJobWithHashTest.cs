
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class RepeatingJobWithHashTestData
{
    public int RunCount { get; set; }
    public int SucceedAfterRuns { get; set; } = 3;
}

public class RepeatingJobWithHashTest(ILogger<RepeatingJobWithHashTest> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("dad155b4-fda5-445a-bc44-79543ba63c61");
    public override string JobType => JobTypeId.ToString();

    public RepeatingJobWithHashTestData JobData { get; private set; } = new ();
    
    //
    
    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running RepeatingJobWithHashTestTest");
        JobData.RunCount++;

        if (JobData.RunCount < JobData.SucceedAfterRuns)
        {
            return Task.FromResult(JobExecutionResult.Repeat(DateTimeOffset.Now.AddMilliseconds(200)));
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
        JobData = OdinSystemSerializer.DeserializeOrThrow<RepeatingJobWithHashTestData>(json);
    }

    //

    public override string? CreateJobHash()
    {
        var text = JobType;
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
