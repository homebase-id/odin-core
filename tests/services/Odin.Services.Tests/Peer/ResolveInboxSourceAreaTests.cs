using NUnit.Framework;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;

namespace Odin.Services.Tests.Peer;

// TODO:INBOX Dual-read transition test. PeerInboxProcessor.ResolveInboxSourceArea picks where a queued inbox item
// is sourced from: the row (LongTerm) for new items, the inbox folder for pre-upgrade items. This pins the
// backward-compat contract that lets old items drain after an upgrade. Delete it together with
// ResolveInboxSourceArea once the inbox folder is drained.
public class ResolveInboxSourceAreaTests
{
    [Test]
    public void RowMetadataPresent_SourcesFromLongTerm()
    {
        var item = new TransferInboxItem { FileMetadata = new FileMetadata() };
        Assert.That(PeerInboxProcessor.ResolveInboxSourceArea(item), Is.EqualTo(StagingArea.LongTerm));
    }

    [Test]
    public void LegacyNullMetadata_FallsBackToInboxFolder()
    {
        var item = new TransferInboxItem { FileMetadata = null };
        Assert.That(PeerInboxProcessor.ResolveInboxSourceArea(item), Is.EqualTo(StagingArea.Inbox));
    }
}
