using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class SimpleJobWithDelayTestData
{
    public string SomeSerializedData { get; set; } = "uninitialized";    
}

public class SimpleJobWithDelayTest(ILogger<SimpleJobWithDelayTest> logger) : AbstractJob
{
    public SimpleJobWithDelayTestData JobData { get; private set; } = new ();
    
    //
    
    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running SimpleJobWithDelayTest");
        await Task.Delay(100, cancellationToken);
        JobData.SomeSerializedData = "hurrah!";
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
        JobData = OdinSystemSerializer.DeserializeOrThrow<SimpleJobWithDelayTestData>(json);
    }

    //
}



