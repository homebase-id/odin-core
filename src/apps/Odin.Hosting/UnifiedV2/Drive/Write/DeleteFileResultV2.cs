using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

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

    /// <summary>
    /// The tombstone's modified time after this request's last change to it, as the change feed shows it; null when
    /// the local file was not deleted
    /// </summary>
    public UnixTimeUtc? Updated { get; set; }

    /// <summary>
    /// Why this file was not deleted, as a camelCase string: "versionTagMismatch" when the request's versionTag is no
    /// longer the file's. Null and left out of the JSON otherwise, so a request without version tags gets the same
    /// response as before.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OdinClientErrorCode? ErrorCode { get; set; }
}