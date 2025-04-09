using Odin.Core.Time;

namespace Odin.Services.Drives.DriveCore.Query;

public class QueryModifiedResultOptions : ResultOptions
{
    public UnixTimeUtc? MaxDate { get; set; }
    public string Cursor { get; set; }

    public static QueryModifiedResultOptions Default()
    {
        return new QueryModifiedResultOptions()
        {
            MaxDate = null,
            ExcludePreviewThumbnail = true,
            MaxRecords = 100,
            Cursor = default,
            IncludeHeaderContent = true
        };
    }
}