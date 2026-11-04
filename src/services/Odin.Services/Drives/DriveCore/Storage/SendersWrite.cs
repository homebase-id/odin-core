using System;
using Odin.Core.Identity;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// A received file's last write by a peer (docs/file-access.md, "Changing a file you sent"): who sent it, the version tag
/// their own copy had, and the one their write gave this copy.  Their tag already there is the same change sent again.
/// While the file still has <see cref="VersionTag"/>, no one else has changed it since, so its original sender changes it
/// without naming a tag.
/// </summary>
public class SendersWrite
{
    public OdinId Sender { get; init; }

    public Guid SendersVersionTag { get; init; }

    public Guid VersionTag { get; init; }
}
