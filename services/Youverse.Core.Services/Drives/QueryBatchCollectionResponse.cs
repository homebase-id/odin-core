using System.Collections.Generic;

namespace Youverse.Core.Services.Drive;

public class QueryBatchCollectionResponse
{
    public QueryBatchCollectionResponse()
    {

        this.Results = new List<QueryBatchResponse>();
    }
    
    public List<QueryBatchResponse> Results { get; set; }
}