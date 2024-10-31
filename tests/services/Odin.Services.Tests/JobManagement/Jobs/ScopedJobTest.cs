using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class ScopedTestDependency
{
    public string Value { get; set; } = "new born";
}

public class ScopedTestData
{
    public string ScopedTestCopy { get; set; } = "uninitialized";
}

public class ScopedJobTest(ILogger<ScopedJobTest> logger, ScopedTestDependency scopedTestDependency) : AbstractJob
{
    public ScopedTestData JobData { get; private set; } = new ();
    
    //
    
    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running ScopedTest");
        JobData.ScopedTestCopy = scopedTestDependency.Value;
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



