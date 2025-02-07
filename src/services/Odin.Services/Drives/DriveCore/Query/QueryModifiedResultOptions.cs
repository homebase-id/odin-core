namespace Odin.Services.Drives.DriveCore.Query;

public class QueryModifiedResultOptions : ResultOptions
{
    public long MaxDate { get; set; }
    public string Cursor { get; set; }

    public static QueryModifiedResultOptions Default()
    {
        return new QueryModifiedResultOptions()
        {
            MaxDate = default,
            ExcludePreviewThumbnail = true,
            MaxRecords = 100,
            Cursor = default,
            IncludeHeaderContent = true
        };
    }
}