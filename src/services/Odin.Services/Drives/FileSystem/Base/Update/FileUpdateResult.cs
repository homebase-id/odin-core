using System;
using System.Collections.Generic;
using Odin.Core.Time;
using Odin.Services.Peer;

namespace Odin.Services.Drives.FileSystem.Base.Update;

public class FileUpdateResult
{
    public ExternalFileIdentifier File { get; init; }

    /// <summary>
    /// The cross reference Id specified by the server if TransitOptions.UseCrossReference == true
    /// </summary>
    public Guid? GlobalTransitId { get; init; }
    
    public GlobalTransitIdFileIdentifier GlobalTransitIdFileIdentifier =>
        GlobalTransitId.HasValue
            ? new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = this.GlobalTransitId.GetValueOrDefault(),
                TargetDrive = this.File.TargetDrive
            }
            : null;
    
    public Dictionary<string, TransferStatus> RecipientStatus { get; init; } = new();
    
    /// <summary>
    /// The version tag to be set on all recipients when they receive and store the file
    /// </summary>
    public Guid NewVersionTag { get; init; }

    /// <summary>
    /// The file's modified time after this update, as the change feed shows it; null for a peer-only update, which
    /// has no local file
    /// </summary>
    public UnixTimeUtc? Updated { get; init; }

    /// <summary>
    /// One entry per payload uploaded in this request (append/overwrite operations); values match the stored file header
    /// </summary>
    public List<PayloadUploadReceipt> Payloads { get; init; } = new();
}