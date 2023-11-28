using System;
using System.Collections.Generic;

namespace Odin.Hosting.Controllers.Base.Drive;

public class DeleteFilesByGroupIdBatchRequest
{
    public List<DeleteFileByGroupIdRequest> Requests { get; set; }
}

public class DeleteFilesByGroupIdBatchResult
{
    public List<DeleteFileByGroupIdResult> Results { get; set; }
}

public class DeleteFileByGroupIdResult
{
    /// <summary>
    /// The groupId of all files to be deleted
    /// </summary>
    public Guid GroupId { get; set; }

    public List<DeleteFileResult> DeleteFileResults { get; set; }
}