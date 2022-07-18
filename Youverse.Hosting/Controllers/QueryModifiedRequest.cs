using Youverse.Core.Services.Drive.Query;

namespace Youverse.Hosting.Controllers;

public class QueryModifiedRequest
{
    public FileQueryParams QueryParams { get; set; }
    public QueryModifiedResultOptions ResultOptions { get; set; }
}