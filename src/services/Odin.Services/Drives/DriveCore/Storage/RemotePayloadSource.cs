using System;
using Odin.Core.Identity;

namespace Odin.Services.Drives.DriveCore.Storage;

public class RemotePayloadSource
{
    public OdinId Identity { get; init; }
    public Guid DriveId { get; init; }

    public bool IsValid()
    {
        return Identity.HasValue() && DriveId != Guid.Empty;
    }
}