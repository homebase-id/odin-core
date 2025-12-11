using System.Collections.Generic;

namespace Odin.Hosting.UnifiedV2.Drive;

public class DeleteFileIdBatchRequestV2
{
    public List<DeleteFileRequestV2> Requests { get; init; }
}