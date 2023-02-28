using Youverse.Core.Services.Drives.DriveCore.Query;

namespace Youverse.Hosting.Controllers;

public class QueryModifiedRequest
{
    public FileQueryParams QueryParams { get; set; }
    public QueryModifiedResultOptions ResultOptions { get; set; }
}