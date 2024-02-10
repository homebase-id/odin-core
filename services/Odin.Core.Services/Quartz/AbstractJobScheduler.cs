using System.Collections.Generic;
using System.Threading.Tasks;
using Quartz;

namespace Odin.Core.Services.Quartz;
#nullable enable

public abstract class AbstractJobScheduler : IJobScheduler
{
    // JobType:
    // - The value of the JobType determines if the job can be scheduled in multiple instances.
    //   Schedules with static JobType values will behave as "singletons" and will not be scheduled more than once,
    //   until the job is completed (or failed).
    // - DO NOT put any sensitive data in the JobType.
    // - BEWARE of using computed ( => ) properties instead of { get; } as the former are not static.
    public abstract string JobType { get; }

    // Create a job and return the job and trigger builders.
    public abstract Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder) where TJob : IJob;

}




