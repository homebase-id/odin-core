using Youverse.Core.Services.Drives.DriveCore.Query;

namespace Youverse.Core.Services.Drives;

public class QueryBatchRequest
{
    public FileQueryParams QueryParams { get; set; }

    public QueryBatchResultOptionsRequest ResultOptionsRequest { get; set; }
}