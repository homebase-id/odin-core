using System;

namespace Odin.Services.JobManagement;

public class JobSchedule
{
    public DateTimeOffset RunAt { get; set; } = DateTimeOffset.Now;
    public int MaxAttempts { get; set; } = 1;
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(5);
    public DateTimeOffset OnSuccessDeleteAfter { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset OnFailureDeleteAfter { get; set; } = DateTimeOffset.Now;
}
