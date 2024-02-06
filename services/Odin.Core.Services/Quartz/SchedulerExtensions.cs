using System.Linq;
using System.Threading.Tasks;
using Quartz;
using Quartz.Impl.Matchers;

namespace Odin.Core.Services.Quartz;
#nullable enable

public static class SchedulerExtensions
{
    public static async Task<bool> JobExists(this ISchedulerFactory schedulerFactory, JobKey jobKey)
    {
        var schedulers = await schedulerFactory.GetAllSchedulers();
        foreach (var scheduler in schedulers)
        {
            if (await scheduler.CheckExists(jobKey))
            {
                return true;
            }
        }
        return false;
    }

    //

    public static string GetGroupName<TJobType>(this IScheduler _)
    {
        return Helpers.GetGroupName<TJobType>();
    }

    //

    public static JobKey CreateTypedJobKey<TJobType>(this IScheduler _, string jobName)
    {
        return Helpers.CreateTypedJobKey<TJobType>(jobName);
    }

    //

    public static JobKey CreateUniqueJobKey<TJobType>(this IScheduler _)
    {
        return Helpers.CreateUniqueJobKey<TJobType>();
    }

    //

    public static JobKey ParseJobKey(this IScheduler _, string jobKey)
    {
        return Helpers.ParseJobKey(jobKey);
    }

    //

    public static async Task<bool> IsJobScheduled(this IScheduler scheduler, JobKey jobKey)
    {
        var triggers = await scheduler.GetTriggersOfJob(jobKey);
        return triggers.Count > 0;
    }

    //

    // Get JobKey of type TJobType that has has least one trigger scheduled.
    // Use this for creating "exclusive" jobs.
    public static async Task<JobKey?> GetScheduledJobKey<TJobType>(this IScheduler scheduler)
    {
        var groupName = scheduler.GetGroupName<TJobType>();
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(groupName));
        foreach (var jobKey in jobKeys)
        {
            var triggers = await scheduler.GetTriggersOfJob(jobKey);
            if (triggers.Count > 0)
            {
                return jobKey;
            }
        }
        return null;
    }

    //

    public static async Task<bool> IsJobRunning(this IScheduler scheduler, JobKey jobKey)
    {
        var currentlyExecutingJobs = await scheduler.GetCurrentlyExecutingJobs();
        return currentlyExecutingJobs.Any(jobContext => jobContext.JobDetail.Key.Equals(jobKey));
    }

    //
}

