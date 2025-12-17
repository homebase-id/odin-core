using System;
using System.Collections.Generic;
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
}