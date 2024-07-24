using System.Collections.Generic;
using System.Threading.Tasks;
using Quartz;

namespace Odin.Services.JobManagement;

public interface OldIJobScheduler
{
    string SchedulingKey { get; }
    Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder) where TJob : IJob;
}
