using System.Collections.Generic;
using Dawn;
using Youverse.Core.Services.Drive.Query;

namespace Youverse.Core.Services.Drive;

public class QueryBatchCollectionResponse
{
    public QueryBatchCollectionResponse()
    {

        this.Results = new List<QueryBatchResponse>();
    }
    
    public List<QueryBatchResponse> Results { get; set; }
}

public class QueryBatchCollectionRequest
{
    public List<CollectionQueryParamSection> Queries { get; set; }
}

public class CollectionQueryParamSection
{
    public string Name { get; set; }

    public FileQueryParams QueryParams { get; set; }

    public QueryBatchResultOptions ResultOptions { get; set; }
    
    public void AssertIsValid()
    {
        Guard.Argument(this.Name, nameof(this.Name)).NotEmpty().NotNull();
        QueryParams.AssertIsValid();
    }
}