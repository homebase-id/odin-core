using System.Collections.Generic;

namespace Odin.Services.Drives;

public class QueryBatchCollectionResponse
{
    public QueryBatchCollectionResponse()
    {

        this.Results = new List<QueryBatchResponse>();
    }
    
    public List<QueryBatchResponse> Results { get; set; }
}