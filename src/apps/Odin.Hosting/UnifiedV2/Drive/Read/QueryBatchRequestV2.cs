using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.UnifiedV2.Drive.Read;

public class QueryBatchRequestV2
{
    public FileQueryParams QueryParams { get; set; }

    public QueryBatchResultOptionsRequest ResultOptionsRequest { get; set; }
    
}