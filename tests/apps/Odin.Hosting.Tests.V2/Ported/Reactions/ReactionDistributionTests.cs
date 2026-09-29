using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.Tests.V2.Ported.Peer;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions.Redux.Group;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Reactions;

/// <summary>
/// Port of <c>_Universal/DriveTests/Reactions/ReactionTestsDistributeToOthers</c>. Reactions that
/// carry <see cref="ReactionTransitOptions"/> must reach every recipient's copy of the file, in both
/// directions: adding one puts it in each recipient's reaction store and reaction preview, and
/// deleting one takes it back out of both everywhere.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> group-reaction endpoints through the in-process host via
/// <see cref="UniversalDriveReactionClient"/>, reached through <c>caller.V1.Reactions</c>.
///
/// <b>Recipient drive permission is now an explicit case parameter.</b> The original read it off
/// <c>IApiClientContext.DrivePermission</c> and granted the sender <c>that | Write</c> on the
/// recipient's drive. <see cref="CallerSpec"/> carries no such property, so each row states the value
/// the original's context would have returned: <see cref="DrivePermission.All"/> for
/// <c>OwnerClientContext</c>, <c>React | Write</c> for the app and guest contexts. The grant the
/// recipients end up with is unchanged.
///
/// <b>Found defect (carried, in the V1 helper this port leaves behind):</b>
/// <c>GuestSpecifyAccessToDrive</c> accepts a <c>TestPermissionKeyList</c> and assigns it to a private
/// field that nothing ever reads — the guest's permission keys are silently dropped. The original's
/// guest row therefore asked for <c>PermissionKeys.UseTransitWrite</c> and never got it, which is why
/// <c>CallerSpec.Guest</c>'s two-argument form is an exact equivalent here rather than a loss. The row
/// still expects <see cref="HttpStatusCode.NotFound"/>, as it did.
///
/// The peer arrange is <b>not</b> gated on <c>expected == OK</c>. The guest row's
/// <see cref="HttpStatusCode.NotFound"/> is a statement about a file that exists and has been
/// distributed — skipping the seed would leave it asserting 404 against a drive with nothing in it,
/// which is a different claim.
///
/// V1's <c>WaitForEmptyOutbox</c> / <c>WaitForEmptyInbox</c> polls are replaced by
/// <see cref="PeerFlow.DistributeAsync(OwnerSession, System.Collections.Generic.IEnumerable{OwnerSession},
/// TargetDrive)"/>: the fast host registers the outbox background service but never starts it, so a
/// passive poll would hang and then throw. The recipient arrange is
/// <see cref="OutboxScenario.PrepareAsync"/>, which builds the same drive, circle and connection the
/// original's inline <c>SetupRecipient</c> did and closes with the same grant check. The trailing
/// <c>DeleteScenario</c> disconnects are dropped — they only restored state, which
/// <see cref="V2Fixture"/>'s per-test reset already guarantees.
/// </remarks>
[TestFixture]
public class ReactionDistributionTests : V2Fixture
{
    /// <summary>Pippin acts; Merry and Samwise receive.</summary>
    protected override string[] HostIdentities => [Identities.Pippin, Identities.Merry, Identities.Sam];

    /// <summary>
    /// The original's three contexts. The middle column is the drive permission the original read off
    /// <c>IApiClientContext.DrivePermission</c> to grant the sender on each recipient's drive.
    /// </summary>
    public static IEnumerable<object[]> ReactionCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), DrivePermission.All, HttpStatusCode.OK];

        yield return
        [
            CallerSpec.App(DriveSpec.Anon(), DrivePermission.React | DrivePermission.Write,
                new[] { PermissionKeys.UseTransitWrite }),
            DrivePermission.React | DrivePermission.Write,
            HttpStatusCode.OK
        ];

        yield return
        [
            CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.React | DrivePermission.Write),
            DrivePermission.React | DrivePermission.Write,
            HttpStatusCode.NotFound
        ];
    }

    [Test]
    [TestCaseSource(nameof(ReactionCases))]
    public async Task CanAddAndDistributeReaction(CallerSpec spec, DrivePermission recipientDrivePermission, HttpStatusCode expected)
    {
        // Setup
        var (caller, local) = await SetupCallerWithOwner(spec);
        var targetDrive = spec.TargetDrive;

        var recipients = new List<OwnerSession>
        {
            await LoginAsOwner(Identities.Merry),
            await LoginAsOwner(Identities.Sam)
        };

        //create the drive on recipients and connect
        foreach (var recipient in recipients)
        {
            await OutboxScenario.PrepareAsync(local, recipient, targetDrive, recipientDrivePermission | DrivePermission.Write);
        }

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, allowDistribution: true);
        var transitOptions = new TransitOptions()
        {
            Recipients = recipients.Select(r => r.Identity.DomainName).ToList()
        };
        var uploadMetadataResponse = await local.V1.Drive.UploadNewMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        var uploadResult = uploadMetadataResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        //
        // ensure the file is sent and is on the recipient's drive
        //
        await PeerFlow.DistributeAsync(local, recipients, targetDrive);

        // Act
        const string reactionContent1 = ":k:";
        var globalTransitFileId = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier();
        var response = await caller.V1.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = globalTransitFileId,
            Reaction = reactionContent1,
            TransitOptions = new ReactionTransitOptions
            {
                Recipients = recipients.Select(r => r.Identity).ToList()
            }
        });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expected));

        if (expected != HttpStatusCode.OK) return;

        foreach (var (_, status) in response.Content.RecipientStatus)
        {
            Assert.That(status, Is.EqualTo(TransferStatus.Enqueued));
        }

        await PeerFlow.DistributeAsync(local, recipients, targetDrive);

        await ReactionAsserts.AssertHasReaction(local, globalTransitFileId, reactionContent1, local.Identity);
        await ReactionAsserts.AssertHasReactionInPreview(local, globalTransitFileId, reactionContent1);
        foreach (var recipient in recipients)
        {
            await ReactionAsserts.AssertHasReaction(recipient, globalTransitFileId, reactionContent1, local.Identity);
            await ReactionAsserts.AssertHasReactionInPreview(recipient, globalTransitFileId, reactionContent1);
        }
    }

    [Test]
    [TestCaseSource(nameof(ReactionCases))]
    public async Task CanDistributeDeleteReaction(CallerSpec spec, DrivePermission recipientDrivePermission, HttpStatusCode expected)
    {
        //
        // Setup
        //
        var (caller, local) = await SetupCallerWithOwner(spec);
        var targetDrive = spec.TargetDrive;

        var recipients = new List<OwnerSession>
        {
            await LoginAsOwner(Identities.Merry),
            await LoginAsOwner(Identities.Sam)
        };

        //
        // create the drive on recipients and connect
        //
        foreach (var recipient in recipients)
        {
            await OutboxScenario.PrepareAsync(local, recipient, targetDrive, recipientDrivePermission | DrivePermission.Write);
        }

        //
        // Send a file
        //

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, allowDistribution: true);
        var transitOptions = new TransitOptions()
        {
            Recipients = recipients.Select(r => r.Identity.DomainName).ToList()
        };

        var uploadMetadataResponse = await local.V1.Drive.UploadNewMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        var uploadResult = uploadMetadataResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        //
        // ensure the file is sent and is on the recipient's drive
        //

        await PeerFlow.DistributeAsync(local, recipients, targetDrive);

        const string reactionContent1 = ":p:";
        var globalTransitFileId = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier();

        var addReactionResponse = await local.V1.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = globalTransitFileId,
            Reaction = reactionContent1,
            TransitOptions = new ReactionTransitOptions()
            {
                Recipients = recipients.Select(r => r.Identity).ToList()
            }
        });

        //
        // Assert valid setup - local and all recipients have the reactions that need to be deleted below
        //
        Assert.That(addReactionResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await PeerFlow.DistributeAsync(local, recipients, targetDrive);

        await ReactionAsserts.AssertHasReaction(local, globalTransitFileId, reactionContent1, local.Identity);
        await ReactionAsserts.AssertHasReactionInPreview(local, globalTransitFileId, reactionContent1);
        foreach (var recipient in recipients)
        {
            await ReactionAsserts.AssertHasReaction(recipient, globalTransitFileId, reactionContent1, local.Identity);
            await ReactionAsserts.AssertHasReactionInPreview(recipient, globalTransitFileId, reactionContent1);
        }

        //
        // Act
        //
        var response = await caller.V1.Reactions.DeleteReaction(new DeleteReactionRequestRedux
        {
            File = globalTransitFileId,
            Reaction = reactionContent1,
            TransitOptions = new ReactionTransitOptions()
            {
                Recipients = recipients.Select(r => r.Identity).ToList()
            }
        });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expected));

        if (expected != HttpStatusCode.OK) return;

        foreach (var (_, status) in response.Content.RecipientStatus)
        {
            Assert.That(status, Is.EqualTo(TransferStatus.Enqueued));
        }

        await PeerFlow.DistributeAsync(local, recipients, targetDrive);

        await ReactionAsserts.AssertDoesNotHaveReactionInPreview(local, globalTransitFileId, reactionContent1);
        await ReactionAsserts.AssertDoesNotHaveReaction(local, globalTransitFileId, reactionContent1, local.Identity);

        foreach (var recipient in recipients)
        {
            await ReactionAsserts.AssertDoesNotHaveReactionInPreview(recipient, globalTransitFileId, reactionContent1);
            await ReactionAsserts.AssertDoesNotHaveReaction(recipient, globalTransitFileId, reactionContent1, local.Identity);
        }
    }
}
