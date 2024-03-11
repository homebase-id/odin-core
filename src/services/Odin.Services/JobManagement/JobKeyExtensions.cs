using System;
using Quartz;

namespace Odin.Services.JobManagement;

public static class JobKeyExtensions
{
    public static SchedulerGroup? SchedulerGroup(this JobKey jobKey)
    {
        var parts = jobKey.Name.Split('|');
        if (parts.Length != 2)
        {
            return null;
        }
        if (Enum.TryParse<SchedulerGroup>(parts[1], out var result))
        {
            return result;
        }
        return null;
    }
}
