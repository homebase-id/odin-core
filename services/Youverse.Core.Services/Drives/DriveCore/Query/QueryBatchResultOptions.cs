using Youverse.Core.Storage.SQLite.DriveDatabase;

namespace Youverse.Core.Services.Drive.Core.Query;

public class QueryBatchResultOptions : ResultOptions
{
    public QueryBatchCursor Cursor { get; set; }
}