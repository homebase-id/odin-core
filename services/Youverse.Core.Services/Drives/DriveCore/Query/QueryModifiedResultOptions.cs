namespace Youverse.Core.Services.Drive.Core.Query;

public class QueryModifiedResultOptions : ResultOptions
{
    public ulong MaxDate { get; set; }
    public ulong Cursor { get; set; }
}