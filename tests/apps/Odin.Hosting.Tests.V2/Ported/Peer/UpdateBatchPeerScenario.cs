using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// The arrange half of the three <c>UpdateBatchWithRecipients*</c> ports, which the originals carried
/// as a copy of <c>SetupRecipients</c> apiece: every recipient hosts the same drive as the sender and
/// grants the sender <see cref="DrivePermission.Write"/> on it through a circle.
/// </summary>
/// <remarks>
/// Not folded into <see cref="PeerFlow"/> because of the one thing that makes this scenario its own:
/// the drive must carry <see cref="BuiltInDriveAttributes.IsCollaborativeChannel"/>. On a
/// collaboration drive <c>PeerFileUpdateWriter.DetermineAclAsync</c> keeps the sending identity's ACL
/// on the recipient's copy instead of narrowing it to owner-only, which is what makes an update fanned
/// out to a peer readable there. <see cref="PeerFlow.CreatePeerDriveAsync"/> and
/// <see cref="OwnerAdmin.EnsureDrive"/> both create drives without attributes, so these fixtures
/// create theirs here and then hand the already-existing drive to the caller build.
/// </remarks>
internal static class UpdateBatchPeerScenario
{
    /// <summary>The drive as the originals created it: anonymous-readable and flagged collaborative.</summary>
    public static async Task CreateCollaborationDrive(OwnerSession owner, TargetDrive drive)
    {
        await owner.Admin.CreateDrive(drive, "Test Drive 001", allowAnonymousReads: true,
            attributes: new Dictionary<string, string>
            {
                { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString }
            });
    }

    /// <summary>
    /// Logs each recipient in, gives it the same collaboration drive, and connects it to the sender
    /// with a circle granting the sender Write on it.
    /// </summary>
    public static async Task<List<OwnerSession>> SetupRecipients(
        OdinHost host,
        OwnerSession sender,
        IEnumerable<string> recipientIdentities,
        TargetDrive drive)
    {
        var sessions = new List<OwnerSession>();
        foreach (var identity in recipientIdentities)
        {
            var recipient = await OwnerSession.LoginAsync(host, identity);
            await CreateCollaborationDrive(recipient, drive);
            await PeerFlow.ConnectAsync(sender, recipient, drive, DrivePermission.Write);
            sessions.Add(recipient);
        }

        return sessions;
    }

    /// <summary>
    /// Stands in for the originals' <c>WaitForEmptyOutbox</c>: that is a passive poll of the outbox
    /// background service, which the fast host registers but never starts. Drains the sender's outbox
    /// (delivering in-process over <c>TestPeerHttpClientFactory</c>) and then processes each
    /// recipient's inbox, which the V1 framework's background services did on their own.
    /// </summary>
    public static async Task Distribute(OwnerSession sender, IEnumerable<OwnerSession> recipients, TargetDrive drive)
    {
        await sender.Sync.DrainOutboxAsync();
        foreach (var recipient in recipients)
        {
            await recipient.Sync.ProcessInboxAsync(drive);
        }
    }
}
