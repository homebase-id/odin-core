using System;
using System.Collections.Generic;
using Odin.Services.Peer;

namespace Odin.Hosting.UnifiedV2.Drive;

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

}