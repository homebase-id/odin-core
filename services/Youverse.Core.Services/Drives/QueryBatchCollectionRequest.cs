using System.Collections.Generic;

namespace Youverse.Core.Services.Drives;

public class QueryBatchCollectionRequest
{
    public List<CollectionQueryParamSection> Queries { get; set; }
}