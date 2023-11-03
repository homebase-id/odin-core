namespace Odin.Core.Services.Quartz;
#nullable enable

public class JobState : IJobState
{
    public JobStatusEnum Status { get; set; } = JobStatusEnum.Unknown;
    public string? Error = null;
}