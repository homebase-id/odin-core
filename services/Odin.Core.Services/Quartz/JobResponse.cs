namespace Odin.Core.Services.Quartz;

#nullable enable

public class JobResponse
{
    public JobStatusEnum Status { get; set; }
    public string? Error { get; set; }
    public string? Data { get; set; }
}
