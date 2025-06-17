using System;
using Odin.Core.Identity;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Defines information on where the file metadata should get payloads, thumbnails, and other data.
/// </summary>
public class DataSubscriptionSource
{
    /// <summary>
    /// The remote identity that has the data
    /// </summary>
    public OdinId Identity { get; init; }
    
    /// <summary>
    /// The drive which holds the data
    /// </summary>
    public Guid DriveId { get; init; }
    
    /// <summary>
    /// When true, the payloads are located at <see cref="Identity"/> on Drive <see cref="DriveId"/>
    /// </summary>
    public bool PayloadsAreRemote { get; init; }
    
    // need to plug in some sort of security settings about
    // how this subscription source works
    
    /// <summary>
    /// When true, z` 
    /// </summary>
    public bool AllowAlternateSenders { get; set; }
    
    /// <summary>
    /// Indicates if the receiving drive should wipe the uniqueId to support a
    /// scenario where it receives files from many sources
    /// </summary>
    public bool RedactUniqueId { get; set; }
    
    public bool IsValid()
    {
        return Identity.HasValue() && DriveId != Guid.Empty;
    }
}