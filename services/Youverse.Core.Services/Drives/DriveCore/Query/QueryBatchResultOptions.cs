using Youverse.Core.Storage.Sqlite.DriveDatabase;

namespace Youverse.Core.Services.Drives.DriveCore.Query;

public class QueryBatchResultOptions : ResultOptions
{
    public QueryBatchCursor Cursor { get; set; }
}