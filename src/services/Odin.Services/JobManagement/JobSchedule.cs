using System;

namespace Odin.Services.JobManagement;

public class JobSchedule
{
    public const int HighPriority = 0;
    public const int LowPriority = int.MaxValue;

    public DateTimeOffset RunAt { get; set; } = DateTimeOffset.Now;
    public int Priority { get; set; } = int.MaxValue / 2; // lower values have higher priority
    public int MaxAttempts { get; set; } = 1;
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan OnSuccessDeleteAfter { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan OnFailureDeleteAfter { get; set; } = TimeSpan.FromDays(1);

    public static JobSchedule Now => new JobSchedule
    {
        RunAt = DateTimeOffset.Now,
    };

    public static JobSchedule FireAndForget => new JobSchedule
    {
        RunAt = DateTimeOffset.Now,
        MaxAttempts = 1,
        OnSuccessDeleteAfter = TimeSpan.Zero,
        OnFailureDeleteAfter = TimeSpan.Zero,
    };
}
