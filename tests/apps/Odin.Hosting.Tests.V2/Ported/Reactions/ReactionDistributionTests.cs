using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
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
/// <c>Sync.DrainOutboxAsync</c> / <c>Sync.ProcessInboxAsync</c>: the fast host registers the outbox
/// background service but never starts it, so a passive poll would hang and then throw. The trailing
/// <c>DeleteScenario</c> disconnects are dropped — they only restored state, which
/// <see cref="V2Fixture"/>'s per-test reset already guarantees.
/// </remarks>
[TestFixture]
public class ReactionDistributionTests : V2Fixture
{
    /// <summary>Pippin acts; Merry and Samwise receive.</summary>
    protected override string[] HostIdentities => [Identities.Pippin, Identities.Merry, Identities.Sam];

    public static IEnumerable<object[]> OwnerAllowed()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), DrivePermission.All, HttpStatusCode.OK];
    }

    public static IEnumerable<object[]> AppAllowedDriveReactOnlyAndUseTransitWrite()
    {
        yield return
        [
            CallerSpec.App(DriveSpec.Anon(), DrivePermission.React | DrivePermission.Write,
                new[] { PermissionKeys.UseTransitWrite }),
            DrivePermission.React | DrivePermission.Write,
            HttpStatusCode.OK
        ];
    }

    public static IEnumerable<object[]> GuestDriveNotFound()
    {
        yield return
        [
            CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.React | DrivePermission.Write),
            DrivePermission.React | DrivePermission.Write,
            HttpStatusCode.NotFound
        ];
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedDriveReactOnlyAndUseTransitWrite))]
    [TestCaseSource(nameof(GuestDriveNotFound))]
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

        //create the drive on recipients
        foreach (var recipient in recipients)
        {
            await local.Connections.SendConnectionRequest(recipient.Identity, new List<Odin.Core.GuidId>());
            await SetupRecipient(recipient, local.Identity, targetDrive, recipientDrivePermission);
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
        await local.Sync.DrainOutboxAsync();
        await ProcessInboxes(recipients, targetDrive);

        // Act
        var callerReactionClient = caller.V1.Reactions;
        const string reactionContent1 = ":k:";
        var response = await callerReactionClient.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
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

        await local.Sync.DrainOutboxAsync();
        await ProcessInboxes(recipients, targetDrive);

        var globalTransitFileId = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier();

        await AssertIdentityHasReaction(local, globalTransitFileId, reactionContent1, local.Identity);
        await AssertIdentityHasReactionInPreview(local, globalTransitFileId, reactionContent1);
        foreach (var recipient in recipients)
        {
            await AssertIdentityHasReaction(recipient, globalTransitFileId, reactionContent1, local.Identity);
            await AssertIdentityHasReactionInPreview(recipient, globalTransitFileId, reactionContent1);
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedDriveReactOnlyAndUseTransitWrite))]
    [TestCaseSource(nameof(GuestDriveNotFound))]
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
        // create the drive on recipients
        //
        foreach (var recipient in recipients)
        {
            await local.Connections.SendConnectionRequest(recipient.Identity, new List<Odin.Core.GuidId>());
            await SetupRecipient(recipient, local.Identity, targetDrive, recipientDrivePermission);
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

        await local.Sync.DrainOutboxAsync();
        await ProcessInboxes(recipients, targetDrive);

        const string reactionContent1 = ":p:";

        var addReactionResponse = await local.V1.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = new ReactionTransitOptions()
            {
                Recipients = recipients.Select(r => r.Identity).ToList()
            }
        });

        //
        // Assert valid setup - local and all recipients have the reactions that need to be deleted below
        //
        Assert.That(addReactionResponse.IsSuccessStatusCode, Is.True);

        await local.Sync.DrainOutboxAsync();
        await ProcessInboxes(recipients, targetDrive);

        var globalTransitFileId = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier();

        await AssertIdentityHasReaction(local, globalTransitFileId, reactionContent1, local.Identity);
        await AssertIdentityHasReactionInPreview(local, globalTransitFileId, reactionContent1);
        foreach (var recipient in recipients)
        {
            await AssertIdentityHasReaction(recipient, globalTransitFileId, reactionContent1, local.Identity);
            await AssertIdentityHasReactionInPreview(recipient, globalTransitFileId, reactionContent1);
        }

        //
        // Act
        //
        var callerReactionClient = caller.V1.Reactions;
        var response = await callerReactionClient.DeleteReaction(new DeleteReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
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

        await local.Sync.DrainOutboxAsync();
        await ProcessInboxes(recipients, targetDrive);

        await AssertIdentityDoesNotHaveReactionInPreview(local, globalTransitFileId, reactionContent1);
        await AssertIdentityDoesNotHaveReaction(local, globalTransitFileId, reactionContent1, local.Identity);

        foreach (var recipient in recipients)
        {
            await AssertIdentityDoesNotHaveReactionInPreview(recipient, globalTransitFileId, reactionContent1);
            await AssertIdentityDoesNotHaveReaction(recipient, globalTransitFileId, reactionContent1, local.Identity);
        }
    }

    private static async Task AssertIdentityDoesNotHaveReactionInPreview(OwnerSession identity, FileIdentifier fileId,
        string reactionContent)
    {
        var getHeaderResponse1 = await identity.V1.Drive.QueryByGlobalTransitId(fileId.ToGlobalTransitIdFileIdentifier());

        var file = getHeaderResponse1.Content.SearchResults.First();
        Assert.That(file.FileMetadata.ReactionPreview.Reactions.Select(pair => pair.Value.ReactionContent),
            Does.Not.Contain(reactionContent));
    }

    private static async Task AssertIdentityHasReactionInPreview(OwnerSession identity, FileIdentifier fileId, string reactionContent)
    {
        var getHeaderResponse1 = await identity.V1.Drive.QueryByGlobalTransitId(fileId.ToGlobalTransitIdFileIdentifier());

        var file = getHeaderResponse1.Content.SearchResults.First();
        Assert.That(file.FileMetadata.ReactionPreview.Reactions.Select(pair => pair.Value.ReactionContent),
            Does.Contain(reactionContent));
    }

    private static async Task AssertIdentityHasReaction(OwnerSession identity, FileIdentifier globalTransitFileId,
        string reactionContent,
        Odin.Core.Identity.OdinId sender,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var getReactionsResponse = await identity.V1.Reactions.GetReactions(new GetReactionsRequestRedux
            {
                File = globalTransitFileId,
            },
            fileSystemType);
        Assert.That(getReactionsResponse.Content.Reactions,
            Has.Exactly(1).Matches<Reaction>(r => r.OdinId == sender && r.ReactionContent == reactionContent));
    }

    private static async Task AssertIdentityDoesNotHaveReaction(OwnerSession identity, FileIdentifier globalTransitFileId,
        string reactionContent,
        Odin.Core.Identity.OdinId sender,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var getReactionsResponse = await identity.V1.Reactions.GetReactions(new GetReactionsRequestRedux
            {
                File = globalTransitFileId,
            },
            fileSystemType);
        Assert.That(getReactionsResponse.Content.Reactions,
            Has.Exactly(0).Matches<Reaction>(r => r.OdinId == sender && r.ReactionContent == reactionContent));
    }

    private static async Task SetupRecipient(OwnerSession recipient, Odin.Core.Identity.OdinId sender, TargetDrive targetDrive,
        DrivePermission drivePermissions)
    {
        //
        // Recipient creates a target drive
        //
        await recipient.Admin.CreateDrive(
            targetDrive,
            "Target drive on recipient",
            allowAnonymousReads: false,
            ownerOnly: false,
            allowSubscriptions: false);

        //
        // Recipient creates a circle with target drive, read and write access
        //
        var expectedPermissionedDrive = new PermissionedDrive()
        {
            Drive = targetDrive,
            Permission = drivePermissions | DrivePermission.Write
        };

        var circleId = Guid.NewGuid();
        await recipient.Admin.CreateCircle(circleId, "Circle with drive access",
            new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = expectedPermissionedDrive
                    }
                }
            });

        //
        // Recipient accepts; grants access to circle
        //
        await recipient.Connections.AcceptConnectionRequest(sender, new List<Odin.Core.GuidId>() { circleId });

        //
        // Test: At this point: recipient should have an ICR record on sender's identity that does not have a key
        //

        var getConnectionInfoResponse = await recipient.Connections.GetConnectionInfo(sender);

        Assert.That(getConnectionInfoResponse.IsSuccessStatusCode, Is.True);
        var senderConnectionInfo = getConnectionInfoResponse.Content;

        Assert.That(senderConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
            cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive)), Is.Not.Null);
    }

    private static async Task ProcessInboxes(List<OwnerSession> recipients, TargetDrive targetDrive)
    {
        foreach (var recipient in recipients)
        {
            await recipient.Sync.ProcessInboxAsync(targetDrive, 100);
        }
    }
}
