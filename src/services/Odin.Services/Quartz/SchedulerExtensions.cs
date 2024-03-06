using System;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Quartz;
using Quartz.Impl.Matchers;

namespace Odin.Services.Quartz;
#nullable enable

public static class SchedulerExtensions
{
    //

    public static string GetGroupName<TJobType>(this IScheduler _)
    {
        return Helpers.GetGroupName<TJobType>();
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

    // Get JobKey of type TJobType that has least one trigger scheduled.
    public static async Task<JobKey?> GetScheduledJobKey<TJobType>(this IScheduler scheduler)
    {
        var groupName = scheduler.GetGroupName<TJobType>();
        return await scheduler.GetScheduledJobKey(groupName);
    }

    //

    // Get JobKey from group name that has least one trigger scheduled.
    public static async Task<JobKey?> GetScheduledJobKey(this IScheduler scheduler, string groupName)
    {
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

    public static async Task UpdateJobMap(
        this IScheduler scheduler,
        IJobDetail jobDetail,
        string key,
        string value)
    {
        if (!jobDetail.Durable)
        {
            throw new OdinSystemException("JobDetail must be durable. Did you forget WithRetention() ?");
        }

        jobDetail.JobDataMap[key] = value;
        await scheduler.AddJob(jobDetail, true); // update JobDataMap
    }

    //

    public static async Task SetJobResponseData(
        this IScheduler scheduler,
        IJobDetail jobDetail,
        object serializableObject)
    {
        var json = OdinSystemSerializer.Serialize(serializableObject);
        await scheduler.UpdateJobMap(jobDetail, JobConstants.JobResponseDataKey, json);
    }

    //


}

