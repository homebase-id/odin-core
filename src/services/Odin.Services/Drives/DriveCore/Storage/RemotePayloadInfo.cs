using System;
using Odin.Core.Identity;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Defines information on where the file metadata should get payloads, thumbnails, and other data.
/// </summary>
public class RemotePayloadInfo
{
    /// <summary>
    /// The remote identity that has the data
    /// </summary>
    public OdinId Identity { get; init; }
    
    /// <summary>
    /// The drive which holds the data
    /// </summary>
    public Guid DriveId { get; init; }
    
    public bool IsValid()
    {
        return Identity.HasValue() && DriveId != Guid.Empty;
    }
}