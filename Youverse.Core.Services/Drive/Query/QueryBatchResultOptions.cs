using Youverse.Core.Storage;
using Youverse.Core.Storage.SQLite;

namespace Youverse.Core.Services.Drive.Query;

public class QueryBatchResultOptions : ResultOptions
{
    public QueryBatchCursor Cursor { get; set; }
}