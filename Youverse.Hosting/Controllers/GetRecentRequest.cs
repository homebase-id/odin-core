using Youverse.Core.Services.Drive.Query;

namespace Youverse.Hosting.Controllers;

public class GetRecentRequest
{
    public FileQueryParams QueryParams { get; set; }
    public GetRecentResultOptions ResultOptions { get; set; }
}