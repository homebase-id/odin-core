using System;
using Odin.Core.Identity;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// A received file's last write by a peer (docs/file-access.md, "Changing a file you sent"): who sent it, the version tag
/// their own copy had, and the one their write gave this copy.  Their tag already there is the same change sent again.
/// While the file still has <see cref="VersionTag"/>, no one else has changed it since, so its original sender changes it
/// without naming a tag.
/// </summary>
public record SendersWrite
{
    public OdinId Sender { get; init; }

    public Guid SendersVersionTag { get; init; }

    public Guid VersionTag { get; init; }

    /// <summary>Whether no one has changed the file since this write: it still has the version tag the write gave it.</summary>
    public bool IsCurrentFor(FileMetadata fileMetadata) => VersionTag == fileMetadata.VersionTag;

    /// <summary>This write, carried over to <paramref name="versionTag"/> by a save that changes none of the file's content.</summary>
    public SendersWrite WithVersionTag(Guid versionTag) => this with { VersionTag = versionTag };
}
