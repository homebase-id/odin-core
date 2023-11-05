using Quartz;

namespace Odin.Core.Services.Quartz;
#nullable enable

public interface IExclusiveJobManager : ITriggerListener
{
    bool Exists(string jobId);
    bool Exists(JobKey jobKey);
    IExclusiveJob? GetJob(string jobId);
    IExclusiveJob? GetJob(JobKey jobKey);
    bool RemoveJob(string jobId);
    bool RemoveJob(JobKey jobKey);
}
