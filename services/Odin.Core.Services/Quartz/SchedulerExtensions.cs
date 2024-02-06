using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Odin.Core.Util;
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
        return TypeName.Sha1<TJobType>().ToHexString();
    }

    //

    public static JobKey CreateTypedJobKey<TJobType>(this IScheduler scheduler, string jobName)
    {
        if (string.IsNullOrWhiteSpace(jobName))
        {
            throw new ArgumentException("Job name cannot be null or empty", nameof(jobName));
        }
        var groupName = scheduler.GetGroupName<TJobType>();
        return new JobKey(jobName, groupName);
    }

    //

    public static JobKey CreateUniqueJobKey<TJobType>(this IScheduler scheduler)
    {
        var jobName = SHA1.HashData(Guid.NewGuid().ToByteArray()).ToHexString();
        return scheduler.CreateTypedJobKey<TJobType>(jobName);
    }

    //

    public static JobKey ParseJobKey(this IScheduler scheduler, string jobKey)
    {
        var jobKeyParts = jobKey.Split('.');
        if (jobKeyParts.Length != 2)
        {
            throw new ArgumentException("Invalid job key", nameof(jobKey));
        }
        return new JobKey(jobKeyParts[1], jobKeyParts[0]);
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

