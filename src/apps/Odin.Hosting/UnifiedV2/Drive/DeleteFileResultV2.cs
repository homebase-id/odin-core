using System;
using System.Collections.Generic;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.UnifiedV2.Drive;

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