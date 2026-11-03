#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Incoming.Drive.Transfer;

namespace Odin.Services.Tests.Peer;

/// <summary>
/// A member's file sent again to a Selective Share drive (docs/file-access.md, "With Selective Share"): the same delivery
/// is the outbox retrying one that landed, taken as done; anything else is a change, which needs a live Write share.
/// </summary>
public class MemberResendTests
{
    [Test]
    public void TheSameDeliveryAgainIsARetry()
    {
        var sent = Sent();
        Assert.That(PeerFileWriter.IsSameDelivery(Copy(sent), Copy(sent)), Is.True);
    }

    [Test]
    public void AnythingElseSentAgainIsAChange()
    {
        var sent = Sent();
        var stored = Copy(sent);
        var changes = new (string what, Action<FileMetadata> change)[]
        {
            ("new content", m => m.AppData.Content = "changed"),
            ("another sender", m => m.SenderOdinId = "pippin.dotyou.cloud"),
            ("another transit id", m => m.GlobalTransitId = Guid.NewGuid()),
            ("a payload uploaded again", m => m.Payloads[0].Uid = UnixTimeUtcUnique.Now()),
            ("another payload", m => m.Payloads.Add(new PayloadDescriptor { Key = "second", Uid = UnixTimeUtcUnique.Now() })),
            ("no sender", m => m.SenderOdinId = null!)
        };

        foreach (var (what, change) in changes)
        {
            var again = Copy(sent);
            change(again);
            Assert.That(PeerFileWriter.IsSameDelivery(stored, again), Is.False, what);
        }

        stored.SenderOdinId = null!;
        Assert.That(PeerFileWriter.IsSameDelivery(stored, Copy(sent)), Is.False, "a stored file with no sender");
    }

    private static FileMetadata Sent() =>
        new()
        {
            GlobalTransitId = Guid.NewGuid(),
            SenderOdinId = "sam.dotyou.cloud",
            AppData = new AppFileMetaData { UniqueId = Guid.NewGuid(), Content = "Sam's file", GroupId = Guid.NewGuid() },
            Payloads = new List<PayloadDescriptor> { new() { Key = "first", Uid = UnixTimeUtcUnique.Now(), BytesWritten = 10 } }
        };

    // Through JSON, as the index gives a header back and a peer's delivery arrives
    private static FileMetadata Copy(FileMetadata metadata) => OdinSystemSerializer.SlowDeepCloneObject(metadata)!;
}
