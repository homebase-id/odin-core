using System.Collections.Generic;
using Odin.Core.Storage;

namespace Odin.Services.Drives;

public class QueryBatchCollectionRequest
{
    public List<CollectionQueryParamSection> Queries { get; set; }
    
    public FileSystemType FileSystemType { get; set; } = FileSystemType.Standard;

}