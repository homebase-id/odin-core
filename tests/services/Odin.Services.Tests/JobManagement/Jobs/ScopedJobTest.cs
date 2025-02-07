using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class ScopedJobTestDependency
{
    public string Value { get; set; } = "new born";
}

public class ScopedTestData
{
    public string ScopedTestCopy { get; set; } = "uninitialized";
}

public class ScopedJobTest(ILogger<ScopedJobTest> logger, ScopedJobTestDependency scopedJobTestDependency) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("1bbbbe79-557f-4990-ab9f-96c4f7869cc2");
    public override string JobType => JobTypeId.ToString();

    public ScopedTestData JobData { get; private set; } = new ();
    
    //
    
    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running ScopedTest");
        JobData.ScopedTestCopy = scopedJobTestDependency.Value;
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
        JobData = OdinSystemSerializer.DeserializeOrThrow<ScopedTestData>(json);
    }

    //
    
}



