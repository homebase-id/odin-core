namespace Odin.Core.Services.Drives.DriveCore.Query;

public class QueryModifiedResultOptions : ResultOptions
{
    public long MaxDate { get; set; }
    public long Cursor { get; set; }
}