using System;
using System.Collections.Generic;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.UnifiedV2.Drive;

public class SendReadReceiptRequestV2 : FileSystemTypeBase
{
    public List<Guid> Files { get; set; }
}

public class DeleteFileOptionsV2 : FileSystemTypeBase
{
    public List<string> Recipients { get; init; }
}


public class DeletePayloadRequestV2 : FileSystemTypeBase
{
    public string Key { get; set; }

    public Guid? VersionTag { get; set; }
}

public class DeleteFileResultV2
{
    public Dictionary<string, DeleteLinkedFileStatus> RecipientStatus { get; set; }

    /// <summary>
    /// Indicates
    /// </summary>
    public bool LocalFileNotFound { get; set; }
    
    /// <summary>
    /// If true, the local file was successfully deleted
    /// </summary>
    public bool LocalFileDeleted { get; set; }

    public Guid FileId { get; set; }
}

public class DeleteFileIdBatchResultV2
{
    public List<DeleteFileResultV2> Results { get; set; }
}

public class DeleteFilesByGroupIdBatchRequestV2: FileSystemTypeBase
{
    public List<DeleteFileByGroupIdRequestV2> Requests { get; set; }
}

public class DeleteFileByGroupIdRequestV2
{
    /// <summary>
    /// The groupId of all files to be deleted
    /// </summary>
    public Guid GroupId { get; set; }

    /// <summary>
    /// List of recipients to receive the delete-file notification
    /// </summary>
    public List<string> Recipients { get; set; }
}

