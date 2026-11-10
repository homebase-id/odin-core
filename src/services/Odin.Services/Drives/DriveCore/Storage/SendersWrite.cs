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

    /// <summary>
    /// Whether no one has changed <paramref name="stored"/> since <paramref name="sender"/>'s last write: the record of it
    /// names them and still has the file's version tag.  A file with no record (stored before it was kept) hasn't.
    /// </summary>
    public static bool UnchangedSinceLastWriteBy(OdinId? sender, ServerFileHeader stored) =>
        stored.ServerMetadata.SendersWrite is { } last && last.Sender == sender && last.IsCurrentFor(stored.FileMetadata);

    /// <summary>This write, carried over to <paramref name="versionTag"/> by a save that changes none of the file's content.</summary>
    public SendersWrite WithVersionTag(Guid versionTag) => this with { VersionTag = versionTag };
}
