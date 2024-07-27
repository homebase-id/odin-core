using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;

namespace Odin.Services.Tests.JobManagement.Jobs;

public class AbortingJobTest(ILogger<AbortingJobTest> logger) : AbstractJob
{

    //
    
    public override Task<RunResult> Run(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running AbortingJobTest");
        return Task.FromResult(RunResult.Abort);
    }
    
    //

    public override string? SerializeJobData()
    {
        return null;
    }
    
    //

    public override void DeserializeJobData(string json)
    {
        // Do nothing
    }

    //
}



