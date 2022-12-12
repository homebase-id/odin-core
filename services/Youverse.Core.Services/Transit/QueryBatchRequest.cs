using Youverse.Core.Services.Drive.Query;

namespace Youverse.Core.Services.Transit;

public class QueryBatchRequest
{
    public FileQueryParams QueryParams { get; set; }

    public QueryBatchResultOptionsRequest ResultOptionsRequest { get; set; }
}

public class TransitQueryBatchRequest : QueryBatchRequest
{
    public string DotYouId { get; set; }
}