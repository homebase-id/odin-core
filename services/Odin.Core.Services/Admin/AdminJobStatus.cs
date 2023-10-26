namespace Odin.Core.Services.Admin;

public enum AdminJobStatus
{
    Unknown,
    Running,
    Paused,
    Completed,
    Blocked,
    Error,
    Scheduled
}