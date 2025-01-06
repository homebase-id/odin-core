using System;
using System.Threading;
using System.Threading.Tasks;

namespace Odin.Services.JobManagement.Jobs;

#nullable enable

public class DeprecatedJob : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("4b353c3a-e1f3-4bea-ac27-0866a738f106");
    public override string JobType => JobTypeId.ToString();

    public override Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        return Task.FromResult(JobExecutionResult.Abort());
    }

    public override string? SerializeJobData()
    {
        return null;
    }

    public override void DeserializeJobData(string json)
    {
    }
}

