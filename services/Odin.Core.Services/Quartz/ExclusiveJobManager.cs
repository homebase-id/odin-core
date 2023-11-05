using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Odin.Core.Services.Quartz;
#nullable enable

public class ExclusiveJobManager : IExclusiveJobManager
{
    private readonly ILogger<ExclusiveJobManager> _logger;
    private readonly object _mutex = new();
    private readonly Dictionary<string, IExclusiveJob> _jobs = new();

    //

    public ExclusiveJobManager(ILogger<ExclusiveJobManager> logger)
    {
        _logger = logger;
    }

    //

    public bool Exists(string jobId)
    {
        lock (_mutex)
        {
            return _jobs.ContainsKey(jobId);
        }
    }

    //

    public bool Exists(JobKey jobKey)
    {
        return Exists(jobKey.ToString());
    }

    //

    public IExclusiveJob? GetJob(string jobId)
    {
        IExclusiveJob? result;
        lock (_mutex)
        {
            _jobs.TryGetValue(jobId, out result);
        }
        return result;
    }

    //

    public IExclusiveJob? GetJob(JobKey jobKey)
    {
        return GetJob(jobKey.ToString());
    }

    //

    public bool RemoveJob(string jobId)
    {
        lock (_mutex)
        {
            return _jobs.Remove(jobId);
        }
    }

    //

    public bool RemoveJob(JobKey jobKey)
    {
        return RemoveJob(jobKey.ToString());
    }

    //

    // This is called by Quartz
    public Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var job = context.JobInstance;
        if (job is not IExclusiveJob exclusiveJob)
        {
            return Task.FromResult(false);
        }

        var key = context.JobDetail.Key.ToString();
        lock (_mutex)
        {
            if (!_jobs.TryGetValue(key, out _))
            {
                _jobs[key] = exclusiveJob;
                return Task.FromResult(false);
            }
        }

        _logger.LogError("Mutex: IExclusiveJob with key {key} already exists. Fix your code.", key);
        return Task.FromResult(true);
    }

    //

    #region Other ITriggerListener stuff

    public Task TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = new CancellationToken())
    {
        return Task.CompletedTask;
    }

    public Task TriggerComplete(
        ITrigger trigger, IJobExecutionContext context,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = new CancellationToken())
    {
        return Task.CompletedTask;
    }

    public string Name => nameof(ExclusiveJobManager);

    #endregion

}