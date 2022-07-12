namespace Youverse.Core.Services.Drive.Query;

public class GetRecentResultOptions : ResultOptions
{
    public ulong MaxDate { get; set; }
    public ulong Cursor { get; set; }
}