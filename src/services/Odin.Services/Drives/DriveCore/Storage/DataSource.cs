using System;
using Odin.Core.Exceptions;
using Odin.Core.Identity;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Defines information on where apps should get the file metadata should get payloads, thumbnails, and other data.
/// </summary>
public class DataSource
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
    /// When true, the payloads should be retrieved from <see cref="Identity"/> and <see cref="DriveId"/>.
    /// When false, the payloads must be sent along with the header (when sending the header over transit)
    /// </summary>
    public bool PayloadsAreRemote { get; init; }

    public bool IsValid()
    {
        return Identity.HasValue() && DriveId != Guid.Empty;
    }

    public void Validate()
    {
        if (!IsValid())
        {
            throw new OdinClientException("Datasource is invalid");
        }
    }
}