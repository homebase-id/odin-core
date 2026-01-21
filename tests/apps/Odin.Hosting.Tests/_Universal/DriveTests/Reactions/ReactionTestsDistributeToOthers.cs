using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions.Group;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests.Reactions;

/// <summary>
/// Tests reactions being distributed to other identities
/// </summary>
public class ReactionTestsDistributeToOthers
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Pippin, TestIdentities.Merry, TestIdentities.Samwise });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }

    public static IEnumerable OwnerAllowed()
    {
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable AppAllowedDriveReactOnlyAndUseTransitWrite()
    {
        yield return new object[]
        {
            new AppSpecifyDriveAccess(TargetDrive.NewTargetDrive(), DrivePermission.React | DrivePermission.Write,
                new TestPermissionKeyList(PermissionKeys.UseTransitWrite)),
            HttpStatusCode.OK
        };
    }

    public static IEnumerable GuestDriveNotFound()
    {
        yield return new object[]
        {
            new GuestSpecifyAccessToDrive(TargetDrive.NewTargetDrive(), DrivePermission.React | DrivePermission.Write,
                new TestPermissionKeyList(PermissionKeys.UseTransitWrite)),
            HttpStatusCode.NotFound
        };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedDriveReactOnlyAndUseTransitWrite))]
    [TestCaseSource(nameof(GuestDriveNotFound))]
    public async Task CanAddAndDistributeReaction(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var localIdentity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(localIdentity);
        var targetDrive = callerContext.TargetDrive;
        var createDriveResponse = await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);
        ClassicAssert.IsTrue(createDriveResponse.IsSuccessStatusCode);
        
        List<TestIdentity> recipients = [TestIdentities.Merry, TestIdentities.Samwise];

        //create the drive on recipients
        foreach (var recipient in recipients)
        {
            await ownerApiClient.Connections.SendConnectionRequest(recipient.OdinId, new List<GuidId>());
            await SetupRecipient(recipient, ownerApiClient.Identity.OdinId, callerContext);
        }

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, allowDistribution: true);
        var transitOptions = new TransitOptions()
        {
            Recipients = recipients.ToStringList()
        };
        var uploadMetadataResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        var uploadResult = uploadMetadataResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        //
        // ensure the file is sent and is on the recipient's drive
        //
        await ownerApiClient.DriveRedux.WaitForEmptyOutbox(targetDrive);
        await WaitForEmptyInboxes(recipients, targetDrive);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(localIdentity.OdinId, callerContext.GetFactory());
        const string reactionContent1 = ":k:";
        var response = await callerReactionClient.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = new ReactionTransitOptions
            {
                Recipients = recipients.ToStringList()
            }
        });

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            foreach (var (_, status) in response.Content.RecipientStatus)
            {
                ClassicAssert.IsTrue(status == TransferStatus.Enqueued);
            }

            await ownerApiClient.DriveRedux.WaitForEmptyOutbox(targetDrive);
            await WaitForEmptyInboxes(recipients, targetDrive);

            var globalTransitFileId = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier();

            await AssertIdentityHasReaction(localIdentity, globalTransitFileId, reactionContent1, localIdentity.OdinId);
            await AssertIdentityHasReactionInPreview(localIdentity, globalTransitFileId, reactionContent1);
            foreach (var recipient in recipients)
            {
                await AssertIdentityHasReaction(recipient, globalTransitFileId, reactionContent1, localIdentity.OdinId);
                await AssertIdentityHasReactionInPreview(recipient, globalTransitFileId, reactionContent1);
            }
        }

        await DeleteScenario(ownerApiClient, recipients);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedDriveReactOnlyAndUseTransitWrite))]
    [TestCaseSource(nameof(GuestDriveNotFound))]
    public async Task CanDistributeDeleteReaction(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        //
        // Setup
        //
        var localIdentity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(localIdentity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        List<TestIdentity> recipients = [TestIdentities.Merry, TestIdentities.Samwise];

        //
        // create the drive on recipients
        //
        foreach (var recipient in recipients)
        {
            await ownerApiClient.Connections.SendConnectionRequest(recipient.OdinId, new List<GuidId>());
            await SetupRecipient(recipient, ownerApiClient.Identity.OdinId, callerContext);
        }

        //
        // Send a file
        //

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, allowDistribution: true);
        var transitOptions = new TransitOptions()
        {
            Recipients = recipients.ToStringList()
        };

        var uploadMetadataResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        var uploadResult = uploadMetadataResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        //
        // ensure the file is sent and is on the recipient's drive
        //

        await ownerApiClient.DriveRedux.WaitForEmptyOutbox(targetDrive);
        await WaitForEmptyInboxes(recipients, targetDrive);


        const string reactionContent1 = ":p:";

        var addReactionResponse = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = new ReactionTransitOptions()
            {
                Recipients = recipients.ToStringList()
            }
        });

        //
        // Assert valid setup - local and all recipients have the reactions that need to be deleted below
        //
        ClassicAssert.IsTrue(addReactionResponse.IsSuccessStatusCode);

        await ownerApiClient.DriveRedux.WaitForEmptyOutbox(targetDrive);
        await WaitForEmptyInboxes(recipients, targetDrive);

        var globalTransitFileId = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier();

        await AssertIdentityHasReaction(localIdentity, globalTransitFileId, reactionContent1, localIdentity.OdinId);
        await AssertIdentityHasReactionInPreview(localIdentity, globalTransitFileId, reactionContent1);
        foreach (var recipient in recipients)
        {
            await AssertIdentityHasReaction(recipient, globalTransitFileId, reactionContent1, localIdentity.OdinId);
            await AssertIdentityHasReactionInPreview(recipient, globalTransitFileId, reactionContent1);
        }

        //
        // Act
        //
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(localIdentity.OdinId, callerContext.GetFactory());
        var response = await callerReactionClient.DeleteReaction(new DeleteReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = new ReactionTransitOptions()
            {
                Recipients = recipients.ToStringList()
            }
        });

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            foreach (var (_, status) in response.Content.RecipientStatus)
            {
                ClassicAssert.IsTrue(status == TransferStatus.Enqueued);
            }

            await ownerApiClient.DriveRedux.WaitForEmptyOutbox(targetDrive);
            await WaitForEmptyInboxes(recipients, targetDrive);

            await AssertIdentityDoesNotHaveReactionInPreview(localIdentity, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(), reactionContent1);
            await AssertIdentityDoesNotHaveReaction(localIdentity, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(), reactionContent1, localIdentity.OdinId);

            foreach (var recipient in recipients)
            {
                await AssertIdentityDoesNotHaveReactionInPreview(recipient, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(), reactionContent1);
                await AssertIdentityDoesNotHaveReaction(recipient, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(), reactionContent1, localIdentity.OdinId);
            }
        }

        await DeleteScenario(ownerApiClient, recipients);
    }

    private async Task AssertIdentityDoesNotHaveReactionInPreview(TestIdentity identity, FileIdentifier fileId, string reactionContent)
    {
        var client = _scaffold.CreateOwnerApiClientRedux(identity);
        var getHeaderResponse1 = await client.DriveRedux.QueryByGlobalTransitId(fileId.ToGlobalTransitIdFileIdentifier());

        var file = getHeaderResponse1.Content.SearchResults.First();
        var noMatchingInReactionPreview = file.FileMetadata.ReactionPreview.Reactions.All(pair => pair.Value.ReactionContent != reactionContent);
        ClassicAssert.IsTrue(noMatchingInReactionPreview);
    }

    private async Task AssertIdentityHasReactionInPreview(TestIdentity identity, FileIdentifier fileId, string reactionContent)
    {
        var client = _scaffold.CreateOwnerApiClientRedux(identity);
        var getHeaderResponse1 = await client.DriveRedux.QueryByGlobalTransitId(fileId.ToGlobalTransitIdFileIdentifier());

        var file = getHeaderResponse1.Content.SearchResults.First();
        var hasReactionInPreview = file.FileMetadata.ReactionPreview.Reactions.Any(pair => pair.Value.ReactionContent == reactionContent);
        ClassicAssert.IsTrue(hasReactionInPreview);
    }

    private async Task AssertIdentityHasReaction(TestIdentity identity, FileIdentifier globalTransitFileId, string reactionContent, OdinId sender,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _scaffold.CreateOwnerApiClientRedux(identity);
        var getReactionsResponse = await client.Reactions.GetReactions(new GetReactionsRequestRedux
            {
                File = globalTransitFileId,
            },
            fileSystemType);
        var hasReactionInDb = getReactionsResponse.Content.Reactions.Any(r => r.OdinId == sender && r.ReactionContent == reactionContent);
        ClassicAssert.IsTrue(hasReactionInDb);
    }

    private async Task AssertIdentityDoesNotHaveReaction(TestIdentity identity, FileIdentifier globalTransitFileId, string reactionContent, OdinId sender,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _scaffold.CreateOwnerApiClientRedux(identity);
        var getReactionsResponse = await client.Reactions.GetReactions(new GetReactionsRequestRedux
            {
                File = globalTransitFileId,
            },
            fileSystemType);
        var reactionNotInDb = getReactionsResponse.Content.Reactions.All(r => !(r.OdinId == sender && r.ReactionContent == reactionContent));
        ClassicAssert.IsTrue(reactionNotInDb);
    }

    private async Task SetupRecipient(TestIdentity recipient, OdinId sender, IApiClientContext callerContext)
    {
        var targetDrive = callerContext.TargetDrive;
        var drivePermissions = callerContext.DrivePermission;
        var recipientClient = _scaffold.CreateOwnerApiClientRedux(recipient);

        //
        // Recipient creates a target drive
        //
        var recipientDriveResponse = await recipientClient.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "Target drive on recipient",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);

        ClassicAssert.IsTrue(recipientDriveResponse.IsSuccessStatusCode);

        //
        // Recipient creates a circle with target drive, read and write access
        //
        var expectedPermissionedDrive = new PermissionedDrive()
        {
            Drive = targetDrive,
            Permission = drivePermissions | DrivePermission.Write
        };

        var circleId = Guid.NewGuid();
        var createCircleResponse = await recipientClient.Network.CreateCircle(circleId, "Circle with drive access", new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = expectedPermissionedDrive
                }
            }
        });

        ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode);


        //
        // Recipient accepts; grants access to circle
        //
        await recipientClient.Connections.AcceptConnectionRequest(sender, new List<GuidId>() { circleId });

        // 
        // Test: At this point: recipient should have an ICR record on sender's identity that does not have a key
        // 

        var getConnectionInfoResponse = await recipientClient.Network.GetConnectionInfo(sender);

        ClassicAssert.IsTrue(getConnectionInfoResponse.IsSuccessStatusCode);
        var senderConnectionInfo = getConnectionInfoResponse.Content;

        ClassicAssert.IsNotNull(senderConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
            cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive)));
    }

    private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, List<TestIdentity> recipients)
    {
        foreach (var recipient in recipients)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipient.OdinId);
        }
    }

    private async Task WaitForEmptyInboxes(List<TestIdentity> recipients, TargetDrive targetDrive)
    {
        foreach (var recipient in recipients)
        {
            var recipientClient = _scaffold.CreateOwnerApiClientRedux(recipient);
            await recipientClient.DriveRedux.ProcessInbox(targetDrive, 100);
            await recipientClient.DriveRedux.WaitForEmptyInbox(targetDrive);
        }
    }
}