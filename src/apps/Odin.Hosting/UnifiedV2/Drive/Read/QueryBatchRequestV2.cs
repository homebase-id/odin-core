using Odin.Services.Drives;

namespace Odin.Hosting.UnifiedV2.Drive.Read;

public class QueryBatchRequestV2
{
    public FileQueryParamsV2 QueryParams { get; set; }

    public QueryBatchResultOptionsRequest ResultOptionsRequest { get; set; }
    
}