using System.Collections.Generic;

namespace Odin.Core.Services.Drives;

public class QueryBatchCollectionRequest
{
    public List<CollectionQueryParamSection> Queries { get; set; }
}