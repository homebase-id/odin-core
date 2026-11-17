using System;
using System.Collections.Generic;
using Odin.Core.Time;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Peer;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class CreateFileResult
{
    public Guid FileId { get; init; }
    public Guid DriveId { get; init; }

    public Guid? GlobalTransitId { get; set; }

    public Dictionary<string, TransferStatus> RecipientStatus { get; init; } = new();

    /// <summary>
    /// The version tag that resulted as of this upload
    /// </summary>
    public Guid NewVersionTag { get; init; }

    /// <summary>
    /// The file's modified time after this request's last change to it, as the change feed shows it
    /// </summary>
    public UnixTimeUtc? Updated { get; init; }

    /// <summary>
    /// When the file got <see cref="NewVersionTag"/>: the change feed's time of the write that gave it, which later writes
    /// that keep the tag (reactions, transfer history, local app data, shares) don't move
    /// </summary>
    public UnixTimeUtc? VersionUpdated { get; init; }

    /// <summary>
    /// One entry per payload uploaded in this request; values match the stored file header
    /// </summary>
    public List<PayloadUploadReceipt> Payloads { get; init; } = new();
}