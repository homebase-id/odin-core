using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite;
using Youverse.Core.Storage.SQLite.DriveDatabase;

namespace Youverse.Core.Services.Drive.Query;

public class QueryBatchResultOptions : ResultOptions
{
    public QueryBatchCursor Cursor { get; set; }
}