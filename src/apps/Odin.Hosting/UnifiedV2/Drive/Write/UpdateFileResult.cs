using System;
using System.Collections.Generic;
using Odin.Core.Time;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Peer;

namespace Odin.Hosting.UnifiedV2.Drive.Write;

public class UpdateFileResult
{
    public Guid FileId { get; init; }
    public Guid DriveId { get; init; }
    
    public Guid? GlobalTransitId { get; init; }
    
    public Dictionary<string, TransferStatus> RecipientStatus { get; init; } = new();
    
    /// <summary>
    /// The version tag to be set on all recipients when they receive and store the file
    /// </summary>
    public Guid NewVersionTag { get; init; }

    /// <summary>
    /// The file's modified time after this request's last change to it, as the change feed shows it; null for a
    /// peer-only update
    /// (<see cref="UpdateLocale.Peer"/>), which has no local file
    /// </summary>
    public UnixTimeUtc? Updated { get; init; }

    /// <summary>
    /// One entry per payload uploaded in this request (append/overwrite operations); values match the stored file header
    /// </summary>
    public List<PayloadUploadReceipt> Payloads { get; init; } = new();

    /// <summary>
    /// The V2 answer for <paramref name="result"/>: with the local file's ids for an update of a local file, without them
    /// for a peer-only one, which has no local file.
    /// </summary>
    public static UpdateFileResult From(FileUpdateResult result, bool withLocalFile) => new()
    {
        FileId = withLocalFile ? result.File.FileId : Guid.Empty,
        DriveId = withLocalFile ? result.File.TargetDrive.Alias.Value : Guid.Empty,
        GlobalTransitId = result.GlobalTransitId,
        RecipientStatus = result.RecipientStatus,
        NewVersionTag = result.NewVersionTag,
        Updated = result.Updated,
        Payloads = result.Payloads
    };
}