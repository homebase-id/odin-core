using System;
using Quartz;

namespace Odin.Services.JobManagement;

public static class OldJobKeyExtensions
{
    public static OldSchedulerGroup? SchedulerGroup(this JobKey jobKey)
    {
        var parts = jobKey.Name.Split('|');
        if (parts.Length != 2)
        {
            return null;
        }
        if (Enum.TryParse<OldSchedulerGroup>(parts[1], out var result))
        {
            return result;
        }
        return null;
    }
}
