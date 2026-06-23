using System;
using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;

namespace Odin.Services.Tests.Peer;

public class TransferInboxItemTests
{
    // The inbox row persists the whole TransferInboxItem serialized into its `value` blob
    // (TransitInboxBoxStorage.AddAsync -> InboxRecord.value; GetPendingItemsAsync deserializes it).
    // Relocating the incoming file's FileMetadata off the inbox-folder `.metadata` file and onto the
    // inbox row is what lets us drop the inbox folder, so the metadata has to survive the exact same
    // serialize/deserialize round-trip the blob goes through. This test is the contract for that.
    [Test]
    public void FileMetadata_RoundTripsThroughInboxValueSerialization()
    {
        var uid = UnixTimeUtcUnique.ZeroTime;
        var original = new TransferInboxItem
        {
            FileId = Guid.NewGuid(),
            DriveId = Guid.NewGuid(),
            Sender = (OdinId)"frodo.dotyou.cloud",
            FileMetadata = new FileMetadata
            {
                GlobalTransitId = Guid.NewGuid(),
                IsEncrypted = true,
                Payloads = new List<PayloadDescriptor>
                {
                    new PayloadDescriptor { Key = "key0001a", Uid = uid, BytesWritten = 123, ContentType = "text/plain" }
                },
                AppData = new AppFileMetaData { Content = "hello" }
            }
        };

        var blob = OdinSystemSerializer.Serialize(original).ToUtf8ByteArray();
        var roundTripped = OdinSystemSerializer.Deserialize<TransferInboxItem>(blob.ToStringFromUtf8Bytes());

        Assert.That(roundTripped!.FileMetadata, Is.Not.Null);
        Assert.That(roundTripped.FileMetadata.GlobalTransitId, Is.EqualTo(original.FileMetadata.GlobalTransitId));
        Assert.That(roundTripped.FileMetadata.IsEncrypted, Is.True);
        Assert.That(roundTripped.FileMetadata.Payloads, Has.Count.EqualTo(1));
        Assert.That(roundTripped.FileMetadata.Payloads[0].Key, Is.EqualTo("key0001a"));
        Assert.That(roundTripped.FileMetadata.Payloads[0].Uid, Is.EqualTo(uid));
        Assert.That(roundTripped.FileMetadata.AppData.Content, Is.EqualTo("hello"));
    }
}
