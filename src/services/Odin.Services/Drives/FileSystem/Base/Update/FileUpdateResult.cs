using System;
using System.Collections.Generic;
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
}