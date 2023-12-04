namespace Odin.Core.Services.Quartz;
#nullable enable

public interface IJobState
{
    public JobStatusEnum Status { get; set; }
    public string? Error { get; set; }
}