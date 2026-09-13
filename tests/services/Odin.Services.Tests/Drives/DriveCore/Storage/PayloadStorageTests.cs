using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

// A payload's stored object path is built from (fileId, Key, Uid). Every "zombie" cleanup in
// DriveStorageServiceBase deletes under the *target* fileId, so a zombie and a live payload that share
// Key and Uid resolve to the exact same object: deleting the zombie destroys the live bytes.
//
// That is not hypothetical. A peer retransmit carries the sender's original payload descriptor, so a
// duplicated delivery of the same file arrives with the same Key and the same Uid as the copy already
// committed. The second inbox item then runs OverwriteFile, moves its bytes onto the same object, and the
// zombie cleanup deletes it. The header survives pointing at storage that no longer exists.
public class PayloadStorageTests
{
    private const string Key = "chat_web0";
    private static readonly UnixTimeUtcUnique Uid = new(117105353993814016);

    private static PayloadDescriptor Payload(string key, UnixTimeUtcUnique uid) => new()
    {
        Key = key,
        Uid = uid,
        ContentType = "image/jpeg",
        Thumbnails = []
    };

    [Test]
    public void ExcludeStillLive_KeepsZombieWhoseStorageIsReusedByTheIncomingPayload()
    {
        // The duplicate peer-delivery case: the retransmit carries the same descriptor, so the "old"
        // payload and the payload the new header points at are one and the same object.
        var existing = new List<PayloadDescriptor> { Payload(Key, Uid) };
        var live = new List<PayloadDescriptor> { Payload(Key, Uid) };

        var deletable = PayloadStorage.ExcludeStillLive(existing, live);

        Assert.That(deletable, Is.Empty);
    }

    [Test]
    public void ExcludeStillLive_DeletesReplacedZombieWhenTheIncomingPayloadHasANewUid()
    {
        // The ordinary overwrite: a fresh upload gets a fresh Uid, so the old bytes sit at a different
        // object and must be reclaimed or they orphan forever.
        var existing = new List<PayloadDescriptor> { Payload(Key, Uid) };
        var live = new List<PayloadDescriptor> { Payload(Key, new UnixTimeUtcUnique(117105353993814017)) };

        var deletable = PayloadStorage.ExcludeStillLive(existing, live);

        Assert.That(deletable.Single().Uid.uniqueTime, Is.EqualTo(Uid.uniqueTime));
    }

    [Test]
    public void ExcludeStillLive_DeletesZombieTheNewHeaderNoLongerReferences()
    {
        var existing = new List<PayloadDescriptor> { Payload("dropped01", Uid) };
        var live = new List<PayloadDescriptor> { Payload(Key, Uid) };

        var deletable = PayloadStorage.ExcludeStillLive(existing, live);

        Assert.That(deletable.Single().Key, Is.EqualTo("dropped01"));
    }

    [Test]
    public void ExcludeStillLive_KeepsZombieWhenTheKeyMatchesButOnlyOneOfSeveralPayloadsCollides()
    {
        var existing = new List<PayloadDescriptor> { Payload(Key, Uid), Payload("dropped01", Uid) };
        var live = new List<PayloadDescriptor> { Payload(Key, Uid) };

        var deletable = PayloadStorage.ExcludeStillLive(existing, live);

        Assert.That(deletable.Single().Key, Is.EqualTo("dropped01"));
    }

    [Test]
    public void ExcludeStillLive_DeletesEverythingWhenTheNewHeaderHasNoPayloads()
    {
        var existing = new List<PayloadDescriptor> { Payload(Key, Uid) };

        var deletable = PayloadStorage.ExcludeStillLive(existing, []);

        Assert.That(deletable.Single().Key, Is.EqualTo(Key));
    }
}
