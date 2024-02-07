using System;
using System.Security.Cryptography;
using Odin.Core.Util;
using Quartz;

namespace Odin.Core.Services.Quartz;
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
        return SHA256.HashData(Guid.NewGuid().ToByteArray()).ToHexString();
    }

    //

    public static JobStatusEnum JobStatusFromStatusValue(string statusValue)
    {
        return statusValue switch
        {
            JobConstants.StatusValueAdded => JobStatusEnum.Scheduled,
            JobConstants.StatusValueStarted => JobStatusEnum.Started,
            JobConstants.StatusValueCompleted => JobStatusEnum.Completed,
            JobConstants.StatusValueFailed => JobStatusEnum.Failed,
            _ => JobStatusEnum.Unknown
        };
    }


}