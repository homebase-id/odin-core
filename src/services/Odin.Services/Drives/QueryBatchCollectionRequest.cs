using System.Collections.Generic;

namespace Odin.Services.Drives;

public class QueryBatchCollectionRequest
{
    public List<CollectionQueryParamSection> Queries { get; set; }
}