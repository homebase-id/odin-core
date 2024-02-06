using System;
using System.Security.Cryptography;
using Odin.Core.Util;
using Quartz;

namespace Odin.Core.Services.Quartz;

public static class Helpers
{
    public static string GetGroupName<TJobType>()
    {
        return TypeName.Sha1<TJobType>().ToHexString();
    }

    //

    public static JobKey CreateTypedJobKey<TJobType>(string jobName)
    {
        if (string.IsNullOrWhiteSpace(jobName))
        {
            throw new ArgumentException("Job name cannot be null or empty", nameof(jobName));
        }
        var groupName = GetGroupName<TJobType>();
        return new JobKey(jobName, groupName);
    }

    //

    public static JobKey CreateUniqueJobKey<TJobType>()
    {
        var jobName = SHA1.HashData(Guid.NewGuid().ToByteArray()).ToHexString();
        return CreateTypedJobKey<TJobType>(jobName);
    }

    //

    public static JobKey ParseJobKey(string jobKey)
    {
        var jobKeyParts = jobKey.Split('.');
        if (jobKeyParts.Length != 2)
        {
            throw new ArgumentException("Invalid job key", nameof(jobKey));
        }
        return new JobKey(jobKeyParts[1], jobKeyParts[0]);
    }

    //


}