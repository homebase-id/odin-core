namespace Odin.Core.Services.Drives.DriveCore.Query;

public class QueryModifiedResultOptions : ResultOptions
{
    public long MaxDate { get; set; }
    public long Cursor { get; set; }

    public static QueryModifiedResultOptions Default()
    {
        return new QueryModifiedResultOptions()
        {
            MaxDate = default,
            ExcludePreviewThumbnail = true,
            MaxRecords = 100,
            Cursor = default,
            IncludeJsonContent = true
        };
    }
}