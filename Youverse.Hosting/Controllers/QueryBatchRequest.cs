using Youverse.Core.Services.Drive.Query;

namespace Youverse.Hosting.Controllers;

public class QueryBatchRequest
{
    public FileQueryParams QueryParams { get; set; }

    public QueryBatchResultOptions ResultOptions { get; set; }
}