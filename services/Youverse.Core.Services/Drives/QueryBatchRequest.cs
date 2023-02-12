using Youverse.Core.Services.Drive.Core.Query;

namespace Youverse.Core.Services.Drive;

public class QueryBatchRequest
{
    public FileQueryParams QueryParams { get; set; }

    public QueryBatchResultOptionsRequest ResultOptionsRequest { get; set; }
}