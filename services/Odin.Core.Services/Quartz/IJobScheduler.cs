using System.Collections.Generic;
using System.Threading.Tasks;
using Quartz;

namespace Odin.Core.Services.Quartz;

public interface IJobScheduler
{
    string JobType { get; }
    Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder) where TJob : IJob;
}
