using System.Collections.Generic;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Controllers.Base.Drive;

public class DeleteFileResult
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

    public ExternalFileIdentifier File { get; set; }
}

public class DeleteFileIdBatchResult
{
    public List<DeleteFileResult> Results { get; set; }
}