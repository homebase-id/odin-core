using System.Collections.Generic;
using Dawn;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit;

namespace Youverse.Core.Services.Drive;

public class QueryBatchCollectionResponse
{
    IEnumerable<QueryBatchResponse>
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