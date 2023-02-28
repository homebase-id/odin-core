namespace Youverse.Core.Services.Drives.DriveCore.Query;

public class QueryModifiedResultOptions : ResultOptions
{
    public ulong MaxDate { get; set; }
    public ulong Cursor { get; set; }
}