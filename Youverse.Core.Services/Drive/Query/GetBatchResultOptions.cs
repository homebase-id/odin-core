using Youverse.Core.Services.Drive.Query.Sqlite.Storage;

namespace Youverse.Core.Services.Drive.Query;

public class GetBatchResultOptions : ResultOptions
{
    public QueryBatchCursor Cursor { get; set; }
}