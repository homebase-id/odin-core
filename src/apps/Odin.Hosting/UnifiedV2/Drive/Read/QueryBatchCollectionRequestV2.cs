using System.Collections.Generic;
using Odin.Core.Storage;
using Odin.Services.Drives;

namespace Odin.Hosting.UnifiedV2.Drive.Read;

public class QueryBatchCollectionRequestV2
{
    public List<CollectionQueryParamSectionV2> Queries { get; set; }
    
    public FileSystemType FileSystemType { get; set; } = FileSystemType.Standard;

}