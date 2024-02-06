using System.Collections.Generic;
using System.Threading.Tasks;
using Quartz;

namespace Odin.Core.Services.Quartz;
#nullable enable

public abstract class AbstractJobScheduler : IJobScheduler
{
    // Return true if the job must not be allowed to have multiple triggers/schedules.
    // Exclusivity are determined by the job's group name.
    // Completed/failed jobs are not considered when determining if a job is exclusive.
    public abstract bool IsExclusive { get; }

    // Create a job and return the job and trigger builders.
    public abstract Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder) where TJob : IJob;
}




