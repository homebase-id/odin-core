using System.Collections.Generic;
using System.Threading.Tasks;
using Quartz;

namespace Odin.Core.Services.Quartz;

public interface IJobScheduler
{
    // Return true if the job must not be allowed to have multiple triggers/schedules.
    // Exclusivity are determined by the job's group name.
    // Completed/failed jobs are not considered when determining if a job is exclusive.
    bool IsExclusive { get; }

    // Schedule a TJob with the given scheduler
    Task<JobKey> Schedule<TJob>(IScheduler scheduler) where TJob : IJob;
    //Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder) where TJob : IJob;
}

//

public abstract class AbstractJobScheduler : IJobScheduler
{
    public abstract bool IsExclusive { get; }
    public abstract Task<JobKey> Schedule<TJob>(IScheduler scheduler) where TJob : IJob;
    //public
}

//






