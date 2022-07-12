using Youverse.Core.Services.Drive.Query;

namespace Youverse.Hosting.Controllers;

public class GetBatchRequest
{
    public  FileQueryParams QueryParams { get; set; }

    public GetBatchQueryResultOptions ResultOptions { get; set; }
}