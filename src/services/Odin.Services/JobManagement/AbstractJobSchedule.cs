using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Quartz;

namespace Odin.Services.JobManagement;
#nullable enable

public abstract class AbstractJobSchedule : IJobScheduler
{
    /// <summary>
    /// Gets the scheduling key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value of the <see cref="SchedulingKey"/> determines if the job can be scheduled in multiple instances on the same scheduler.
    /// Schedules with static <see cref="SchedulingKey"/> values will behave as "singletons" and will not be scheduled more than once,
    /// until the job is completed (or failed).
    /// </para>
    /// <para>DO NOT put any sensitive data in the <see cref="SchedulingKey"/>.</para>
    /// <para>BEWARE of using computed ( <c>=&gt;</c> ) propery instead of <c>{ get; }</c> as the former is not static.</para>
    /// </remarks>
    public abstract string SchedulingKey { get; }

    public abstract SchedulerGroup SchedulerGroup { get; }

    /// <summary>
    /// Create a job and return the job and trigger builders.
    /// </summary>
    public abstract Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder) where TJob : IJob;

    /// <summary>
    /// Unique job key for this schedule.
    /// </summary>
    public JobKey JobKey
    {
        get
        {
            if (_jobKey == null)
            {
                lock (_lock)
                {
                    if (_jobKey == null)
                    {
                        if (SchedulingKey.Contains('.') || SchedulingKey.Contains('|'))
                        {
                            throw new ArgumentException("SchedulingKey must not contain '.' nor '|'");
                        }

                        var schedulerGroup = SchedulerGroup.ToString();
                        if (schedulerGroup.Contains('.') || schedulerGroup.Contains('|'))
                        {
                            throw new ArgumentException("SchedulerGroup must not contain '.' nor '|'");
                        }

                        var jobInstance = Helpers.UniqueId();
                        if (jobInstance.Contains('.') || jobInstance.Contains('|'))
                        {
                            throw new ArgumentException("JobInstance must not contain '.' nor '|'");
                        }

                        var jobName = $"{jobInstance}|{schedulerGroup}";
                        _jobKey = new JobKey(jobName, SchedulingKey);
                    }
                }
            }
            return _jobKey;
        }
    }
    private readonly object _lock = new();
    private JobKey? _jobKey;
}




