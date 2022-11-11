using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit;

namespace Youverse.Hosting.Controllers;

public class QueryBatchRequest
{
    public FileQueryParams QueryParams { get; set; }

    public QueryBatchResultOptionsRequest ResultOptionsRequest { get; set; }
}