namespace Odin.Services.Quartz;

public enum JobStatus
{
    Unknown,
    NotFound,
    Scheduled,
    Started,
    Completed,
    Failed
};
