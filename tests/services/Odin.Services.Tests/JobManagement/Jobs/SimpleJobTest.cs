using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class SimpleJobTestData
{
    public string SomeJobData { get; set; } = "uninitialized";    
}

public class SimpleJobTest(ILogger<SimpleJobTest> logger) : AbstractJob
{
    public SimpleJobTestData JobData { get; private set; } = new ();
    
    //
    
    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running SimpleJobTest");
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
        JobData = OdinSystemSerializer.DeserializeOrThrow<SimpleJobTestData>(json);
    }

    //
}



