using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Quartz;

namespace Odin.Core.Services.Quartz;
#nullable enable

public abstract class AbstractJobScheduler : IJobScheduler
{
    // The JobId should be unique per job.
    // If you want to make sure the job cannot be scheduled more than once, override this with a static value.
    // Once the job is completed (or failed), the JobId can be scheduled again.
    // DO NOT put any sensitive data in the JobId.
    public abstract string JobId { get; }

    // Create a job and return the job and trigger builders.
    public abstract Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder) where TJob : IJob;
}




