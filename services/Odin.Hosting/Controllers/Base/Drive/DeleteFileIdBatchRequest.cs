using System.Collections.Generic;

namespace Odin.Hosting.Controllers.Base.Drive;

public class DeleteFileIdBatchRequest
{
    public List<DeleteFileRequest> Requests { get; set; }
}