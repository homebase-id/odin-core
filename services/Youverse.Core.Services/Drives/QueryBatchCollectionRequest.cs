using System.Collections.Generic;

namespace Youverse.Core.Services.Drive;

public class QueryBatchCollectionRequest
{
    public List<CollectionQueryParamSection> Queries { get; set; }
}