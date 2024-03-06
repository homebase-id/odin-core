using System.Collections.Generic;
using System.Threading.Tasks;
using Quartz;

namespace Odin.Core.Services.Quartz;
#nullable enable

public abstract class AbstractJobScheduler : IJobScheduler
{
    /// <summary>
    /// Gets the scheduling key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value of the <see cref="SchedulingKey"/> determines if the job can be scheduled in multiple instances.
    /// Schedules with static <see cref="SchedulingKey"/> values will behave as "singletons" and will not be scheduled more than once,
    /// until the job is completed (or failed).
    /// </para>
    /// <para>DO NOT put any sensitive data in the <see cref="SchedulingKey"/>.</para>
    /// <para>BEWARE of using computed ( <c>=&gt;</c> ) propery instead of <c>{ get; }</c> as the former is not static.</para>
    /// </remarks>
    public abstract string SchedulingKey { get; }

    /// <summary>
    /// Create a job and return the job and trigger builders.
    /// </summary>
    public abstract Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder) where TJob : IJob;
}




