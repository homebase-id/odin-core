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
/// A member's file sent again to a Selective Share drive (docs/selective-share-pr3-plan.md, section 7): the same delivery
/// is the outbox retrying one that landed, taken as done; anything else is a change, which goes through an update.
/// </summary>
public class MemberResendTests
{
    [Test]
    public void TheSameDeliveryAgainIsARetry()
    {
        var sent = Sent();
        Assert.That(PeerFileWriter.IsSameDelivery(Stored(sent), Sent(sent)), Is.True);
    }

    [Test]
    public void AnythingElseSentAgainIsAChange()
    {
        var sent = Sent();
        var stored = Stored(sent);
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
            var again = Sent(sent);
            change(again);
            Assert.That(PeerFileWriter.IsSameDelivery(stored, again), Is.False, what);
        }

        stored.SenderOdinId = null!;
        Assert.That(PeerFileWriter.IsSameDelivery(stored, Sent(sent)), Is.False, "a stored file with no sender");
    }

    private static FileMetadata Sent(FileMetadata? copyOf = null) => copyOf != null
        ? OdinSystemSerializer.Deserialize<FileMetadata>(OdinSystemSerializer.Serialize(copyOf))!
        : new FileMetadata
        {
            GlobalTransitId = Guid.NewGuid(),
            SenderOdinId = "sam.dotyou.cloud",
            AppData = new AppFileMetaData { UniqueId = Guid.NewGuid(), Content = "Sam's file", GroupId = Guid.NewGuid() },
            Payloads = new List<PayloadDescriptor> { new() { Key = "first", Uid = UnixTimeUtcUnique.Now(), BytesWritten = 10 } }
        };

    // As the index gives it back: the header's AppData through its JSON column
    private static FileMetadata Stored(FileMetadata sent)
    {
        var stored = Sent(sent);
        stored.AppData = OdinSystemSerializer.Deserialize<AppFileMetaData>(OdinSystemSerializer.Serialize(sent.AppData))!;
        return stored;
    }
}
