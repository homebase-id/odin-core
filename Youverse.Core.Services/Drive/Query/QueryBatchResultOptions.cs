using Youverse.Core.Storage;

namespace Youverse.Core.Services.Drive.Query;

public class QueryBatchResultOptions : ResultOptions
{
    public QueryBatchCursor Cursor { get; set; }
}