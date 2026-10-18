using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Odin.Services.Util;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class SoftDeleteBatchRequestV2
{
    /// <summary>
    /// The files to delete, leaving a tombstone in the change feed. 1 to <see cref="FileVersionTagBatchV2.MaxFiles"/>,
    /// none twice.
    /// </summary>
    public List<SoftDeleteFileV2> Files { get; init; }

    /// <summary>
    /// A malformed batch is refused whole (400) before any file is touched: no files, too many, a file twice, an
    /// empty fileId or versionTag, or a recipient that is not a valid identity
    /// </summary>
    public void AssertValid(OdinId tenant)
    {
        OdinValidationUtils.AssertNotNull(Files, "files");
        OdinValidationUtils.AssertIsTrue(Files.Count is > 0 and <= FileVersionTagBatchV2.MaxFiles,
            $"files must hold 1 to {FileVersionTagBatchV2.MaxFiles} entries");
        OdinValidationUtils.AssertIsTrue(Files.All(f => f != null && f.FileId != Guid.Empty && f.VersionTag != Guid.Empty),
            "every file needs a fileId, and a versionTag when it names one");
        OdinValidationUtils.AssertIsTrue(Files.Select(f => f.FileId).Distinct().Count() == Files.Count,
            "a file may appear only once");
        foreach (var f in Files.Where(f => f.Recipients != null))
        {
            OdinValidationUtils.AssertIsTrue(f.Recipients.All(r => r != null), "a recipient cannot be null");
            OdinValidationUtils.AssertValidRecipientList(f.Recipients, allowEmpty: true, tenant);
        }
    }
}

public class SoftDeleteFileV2
{
    public Guid FileId { get; init; }

    /// <summary>
    /// Optional. When set, the file is only deleted if it is still at this version; otherwise its result carries
    /// errorCode "versionTagMismatch", nothing is sent to its recipients, and the rest of the batch goes ahead.
    /// </summary>
    public Guid? VersionTag { get; init; }

    /// <summary>
    /// Optional. The identities that hold a copy of this file and are told to delete theirs.
    /// </summary>
    public List<string> Recipients { get; init; }
}

public class SoftDeleteBatchResultV2
{
    /// <summary>
    /// One entry per requested file, in request order.
    /// </summary>
    public List<SoftDeleteResultV2> Results { get; init; }
}

public class SoftDeleteResultV2
{
    public Guid FileId { get; init; }

    /// <summary>
    /// Null when the file is deleted, including one that already was. Otherwise why it was left alone, as a camelCase
    /// string: "versionTagMismatch" (written since the caller read it), "fileNotFound" (missing, or hidden from the
    /// caller by its ACL), or any other client error. One file's error never stops the others.
    /// </summary>
    public OdinClientErrorCode? ErrorCode { get; init; }

    /// <summary>
    /// The tombstone's modified time, as the change feed shows it; null when the file was not deleted
    /// </summary>
    public UnixTimeUtc? Updated { get; init; }

    /// <summary>
    /// Per recipient: "enqueued", or "enqueueFailed" for one the caller is no longer connected to. Recipients are told
    /// before the local delete, so if that delete then fails (an edit landing in between) this still shows who was told.
    /// </summary>
    public Dictionary<string, DeleteLinkedFileStatus> RecipientStatus { get; init; } = new();
}
