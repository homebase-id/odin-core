using System;
using NUnit.Framework;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

namespace Odin.Services.Tests.Peer;

public class PeerFileSendTimeoutTests
{
    private static readonly OdinConfiguration.HostSection Host = new() { InboxOutboxRecoveryAgeSeconds = 24 * 60 * 60, PeerOperationMaxAttempts = 3 };

    [Test]
    public void AFileWithoutPayloadsHasFiveMinutes()
    {
        Assert.That(PeerFileSendTimeout.For(new ServerFileHeader { FileMetadata = new FileMetadata() }, Host), Is.EqualTo(TimeSpan.FromMinutes(5)));
    }

    [Test]
    public void EachMebibyteOfPayloadOrThumbnailAddsASecond()
    {
        var header = new ServerFileHeader { FileMetadata = new FileMetadata
        {
            Payloads =
            [
                new PayloadDescriptor
                {
                    BytesWritten = 4L * 1024 * 1024 * 1024,
                    Thumbnails = [new ThumbnailDescriptor { BytesWritten = 1024 * 1024 }]
                },
                new PayloadDescriptor { BytesWritten = 1024 * 1024 }
            ]
        } };

        Assert.That(PeerFileSendTimeout.For(header, Host), Is.EqualTo(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(4 * 1024 + 2)),
            "4 GiB and two MiB: an hour and a quarter");
    }

    [Test]
    public void EveryAttemptEndsBeforeTheOutboxTakesItsItemForDead()
    {
        var header = new ServerFileHeader { FileMetadata = new FileMetadata { Payloads = [new PayloadDescriptor { BytesWritten = 50L * 1000 * 1000 * 1000 }] } };

        Assert.That(PeerFileSendTimeout.For(header, Host), Is.EqualTo(TimeSpan.FromHours(6)), "a day over three attempts and a spare");
    }
}
