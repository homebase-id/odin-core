using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class SimpleJobWithDelayTestData
{
    public string SomeSerializedData { get; set; } = "uninitialized";
    public string SomeOtherData { get; set; } = "";
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(100);
}

public class SimpleJobWithDelayTest(ILogger<SimpleJobWithDelayTest> logger) : AbstractJob
{
    public SimpleJobWithDelayTestData JobData { get; private set; } = new ();
    
    //
    
    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation($"Running SimpleJobWithDelayTest:{JobData.SomeOtherData}");
        await Task.Delay(JobData.Delay, cancellationToken);
        JobData.SomeSerializedData = "hurrah!";
        logger.LogInformation($"Stopped SimpleJobWithDelayTest:{JobData.SomeOtherData}");
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



