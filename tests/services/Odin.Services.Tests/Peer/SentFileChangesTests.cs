#nullable enable
using System;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Incoming.Drive.Transfer;

namespace Odin.Services.Tests.Peer;

/// <summary>
/// What a change of a sent file names (docs/file-access.md, "Changing a file you sent"): its original sender changes it
/// while no one else has changed it since their last write, naming no tag; a peer's tag already recorded is the same change
/// sent again; anyone else, or the sender once someone else changed it, names the version tag the file has.
/// </summary>
public class SentFileChangesTests
{
    private static readonly OdinId Sam = new("sam.dotyou.cloud");
    private static readonly OdinId Pippin = new("pippin.dotyou.cloud");
    private static readonly Guid Stored = Guid.NewGuid();
    private static readonly Guid SamsLast = Guid.NewGuid();

    [Test]
    public void APeersTagAlreadyRecordedIsTheSameChangeAgain()
    {
        Assert.That(SentFileChanges.AlreadyLandedOrAssertMayChange(Header(Stored), Sam, sentIt: true, SamsLast, null), Is.True,
            "its sender's");
        Assert.That(SentFileChanges.AlreadyLandedOrAssertMayChange(Header(Guid.NewGuid()), Sam, sentIt: false, SamsLast, null), Is.True,
            "a member's, though the file changed since");
    }

    [Test]
    public void ItsSenderChangesItNamingNoTagWhileNoOneElseChangedIt() =>
        Assert.That(SentFileChanges.AlreadyLandedOrAssertMayChange(Header(Stored), Sam, sentIt: true, Guid.NewGuid(), null), Is.False);

    [Test]
    public void AChangeNamingTheFilesTagGoesOn()
    {
        Assert.That(SentFileChanges.AlreadyLandedOrAssertMayChange(Header(Stored), Pippin, sentIt: false, Guid.NewGuid(), Stored),
            Is.False, "a member's");
        var someoneElses = Guid.NewGuid();
        Assert.That(SentFileChanges.AlreadyLandedOrAssertMayChange(Header(someoneElses), Sam, sentIt: true, Guid.NewGuid(), someoneElses),
            Is.False, "its sender's, once someone else changed it");
    }

    [Test]
    public void AnythingElseIsRefused()
    {
        foreach (var (what, header, peer, sentIt, named) in new (string, ServerFileHeader, OdinId, bool, Guid?)[]
                 {
                     ("a member's, naming another tag", Header(Stored), Pippin, false, Guid.NewGuid()),
                     ("a member's, naming none", Header(Stored), Pippin, false, null),
                     ("its sender's, once someone else changed it", Header(Guid.NewGuid()), Sam, true, null),
                     ("its sender's, after another peer's write", Header(Stored, Pippin), Sam, true, null),
                     ("its sender's, with no write of theirs recorded",
                         new ServerFileHeader { FileMetadata = new FileMetadata { VersionTag = Stored } }, Sam, true, null)
                 })
        {
            var refused = Assert.Throws<OdinClientException>(
                () => SentFileChanges.AlreadyLandedOrAssertMayChange(header, peer, sentIt, Guid.NewGuid(), named), what);
            Assert.That(refused!.ErrorCode, Is.EqualTo(OdinClientErrorCode.VersionTagMismatch), what);
        }
    }

    [Test]
    public void AnotherPeersTagIsNotTheSameChangeAgain() =>
        Assert.Throws<OdinClientException>(() =>
            SentFileChanges.AlreadyLandedOrAssertMayChange(Header(Guid.NewGuid()), Pippin, sentIt: false, SamsLast, null));

    // A file whose last peer write, <paramref name="writer"/>'s (Sam's by default) of their tag SamsLast, gave it Stored;
    // it has <paramref name="versionTag"/> now
    private static ServerFileHeader Header(Guid versionTag, OdinId? writer = null) => new()
    {
        FileMetadata = new FileMetadata
        {
            VersionTag = versionTag,
            SendersWrite = new SendersWrite { Sender = writer ?? Sam, SendersVersionTag = SamsLast, VersionTag = Stored }
        }
    };
}
