using System;
using Odin.Core;
using Odin.Core.Util;
using Quartz;

namespace Odin.Services.JobManagement;
#nullable enable

public static class Helpers
{
    public static string GetGroupName<TJobType>()
    {
        return TypeName.Sha1<TJobType>().ToHexString();
    }

    //

    // The string version of a jobkey is "group-name"."job-name".
    // We allow group-name to include '.', but not job-name.
    public static JobKey ParseJobKey(string jobKey)
    {
        var lastDotIndex = jobKey.LastIndexOf('.');
        if (lastDotIndex < 1 || lastDotIndex == jobKey.Length - 1)
        {
            throw new ArgumentException($"Invalid job key: '{jobKey}'");
        }
        var groupName = jobKey[..lastDotIndex];
        var jobName = jobKey[(lastDotIndex + 1)..];

        return new JobKey(jobName, groupName);
    }

    //

    public static JobKey CreateUniqueJobKey()
    {
        return new JobKey(UniqueId(), UniqueId());
    }

    //

    public static string UniqueId()
    {
        return Guid.NewGuid().ToString("N");
    }

    //

    public static JobStatus JobStatusFromStatusValue(string statusValue)
    {
        return statusValue switch
        {
            JobConstants.StatusValueAdded => JobStatus.Scheduled,
            JobConstants.StatusValueStarted => JobStatus.Started,
            JobConstants.StatusValueCompleted => JobStatus.Completed,
            JobConstants.StatusValueFailed => JobStatus.Failed,
            _ => JobStatus.Unknown
        };
    }


}