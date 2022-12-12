namespace Youverse.Core.Services.Drive.Query;

public class QueryModifiedResultOptions : ResultOptions
{
    public ulong MaxDate { get; set; }
    public ulong Cursor { get; set; }
}