using System;
using System.Collections.Generic;

namespace Odin.Hosting.UnifiedV2.Drive;

public class DeleteFilesByGroupIdBatchRequestV2 
{
    public List<DeleteFileByGroupIdRequestV2> Requests { get; set; }
}

public class DeleteFilesByGroupIdBatchResultV2
{
    public List<DeleteFileByGroupIdResultV2> Results { get; set; }
}

public class DeleteFileByGroupIdResultV2
{
    /// <summary>
    /// The groupId of all files to be deleted
    /// </summary>
    public Guid GroupId { get; set; }

    public List<DeleteFileResultV2> DeleteFileResults { get; set; }
}