using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Outbox/OutboxProcessingSingleRecipientTestsSuccessScenarios.cs
///
/// A send to one connected recipient that succeeds: the recipient gets a decryptable copy and the
/// sender's transfer history records the delivery — and the two read paths that can leave that
/// history out of their results (<c>QueryBatch</c> and <c>QueryModified</c> with
/// <c>IncludeTransferHistory = false</c>).
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>The original's <c>TestCases()</c> had one live row,
/// <c>OwnerClientContext(TargetDrive.NewTargetDrive())</c>, with two commented-out siblings kept
/// verbatim below. One row is not a matrix, so the first test is a plain <c>[Test]</c> and its
/// always-true <c>if (expectedStatusCode == OK)</c> guard is gone; the other three tests never had a
/// matrix.</item>
/// <item>The caller the matrix built was <b>unused</b> beyond <c>WaitForEmptyOutbox</c>: every
/// assertion ran through the sender's own owner client. Nothing here builds a caller.</item>
/// <item><c>WaitForEmptyOutbox</c> becomes <c>Sync.DrainOutboxAsync()</c> — the V1 call is a passive
/// poll on the outbox background service, which the fast host registers but never starts. In the
/// last two tests the original drained <i>before</i> asserting on the upload response; the drain
/// moves after the upload assertions here, which is where it belongs and changes nothing.</item>
/// <item><c>CanSetDependencyIdOnOutboxItem</c> is carried with its <c>[Ignore]</c> and its empty
/// body verbatim.</item>
/// <item>Trailing <c>DeleteScenario</c> calls were cleanup only and are dropped — per-test reset
/// covers them.</item>
/// </list>
/// </remarks>
[TestFixture]
public class OutboxProcessingSingleRecipientTestsSuccessScenarios : V2Fixture
{
    private const string UploadedContent = "pie";

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    // The original's live row was [OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK]
    // — one row, so this is a plain [Test]. Its two commented-out siblings, kept verbatim:
    //   GuestWriteOnlyAccessToDrive -> HttpStatusCode.MethodNotAllowed
    //   AppWriteOnlyAccessToDrive   -> HttpStatusCode.OK
    [Test]
    public async Task RecipientTransferHistoryOnSenderIsUpdatedWhenTransferringFileToSingleRecipient()
    {
        var senderOwnerClient = await LoginAsOwner(Identities.Frodo);
        var recipientOwnerClient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.Write;

        var targetDrive = TargetDrive.NewTargetDrive();
        await OutboxScenario.PrepareAsync(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

        var transitOptions = new TransitOptions
        {
            Recipients = [recipientOwnerClient.Identity.DomainName]
        };

        var (uploadResponse, encryptedJsonContent64) =
            await UploadEncryptedMetadataAsync(senderOwnerClient, targetDrive, transitOptions);

        Assert.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uploadResult = uploadResponse.Content;
        Assert.That(uploadResult.RecipientStatus.Count, Is.EqualTo(1));
        Assert.That(uploadResult.RecipientStatus[recipientOwnerClient.Identity], Is.EqualTo(TransferStatus.Enqueued));

        await senderOwnerClient.Sync.DrainOutboxAsync();

        // validate recipient got the file
        await recipientOwnerClient.Sync.ProcessInboxAsync(uploadResult.File.TargetDrive);

        var recipientFileResponse =
            await recipientOwnerClient.V1.Drive.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
        Assert.That(recipientFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var recipientFile = recipientFileResponse.Content.SearchResults.SingleOrDefault();
        Assert.That(recipientFile, Is.Not.Null);
        Assert.That(recipientFile.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));

        //
        // Validate the transfer history was updated correctly
        //
        var getHistoryResponse = await senderOwnerClient.V1.Drive.GetTransferHistory(uploadResult.File);
        Assert.That(getHistoryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var theHistory = getHistoryResponse.Content;
        Assert.That(theHistory, Is.Not.Null);
        var recipientStatus = theHistory.GetHistoryItem(recipientOwnerClient.Identity);

        Assert.That(recipientStatus, Is.Not.Null, "There should be a status update for the recipient");
        Assert.That(recipientStatus.IsInOutbox, Is.False);
        Assert.That(recipientStatus.IsReadByRecipient, Is.False);
        Assert.That(recipientStatus.LatestTransferStatus, Is.EqualTo(LatestTransferStatus.Delivered));
        Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.EqualTo(uploadResult.NewVersionTag));
    }

    [Test]
    public async Task GetBatchOnSenderCanExcludeRecipientTransferHistory()
    {
        var senderOwnerClient = await LoginAsOwner(Identities.Frodo);
        var recipientOwnerClient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.Write;

        var targetDrive = TargetDrive.NewTargetDrive();
        await OutboxScenario.PrepareAsync(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

        const int fileType = 1033;
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new()
            {
                Content = UploadedContent,
                FileType = fileType,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.Connected
        };

        var storageOptions = new StorageOptions
        {
            Drive = targetDrive
        };

        var transitOptions = new TransitOptions
        {
            Recipients = [recipientOwnerClient.Identity.DomainName]
        };

        var (uploadResponse, _) = await senderOwnerClient.V1.Drive.UploadNewEncryptedMetadata(
            fileMetadata,
            storageOptions,
            transitOptions
        );

        Assert.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uploadResult = uploadResponse.Content;
        Assert.That(uploadResult.RecipientStatus.Count, Is.EqualTo(1));
        Assert.That(uploadResult.RecipientStatus[recipientOwnerClient.Identity], Is.EqualTo(TransferStatus.Enqueued));

        await senderOwnerClient.Sync.DrainOutboxAsync();

        // Assert: file that was sent has peer transfer status updated
        var getHistoryResponse = await senderOwnerClient.V1.Drive.GetTransferHistory(uploadResult.File);
        Assert.That(getHistoryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var theHistory = getHistoryResponse.Content;
        Assert.That(theHistory, Is.Not.Null);
        var recipientStatus = theHistory.GetHistoryItem(recipientOwnerClient.Identity);
        Assert.That(recipientStatus, Is.Not.Null, "There should be a status update for the recipient");
        Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.EqualTo(uploadResult.NewVersionTag));

        var request = new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = targetDrive,
                FileType = [fileType]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                CursorState = null,
                MaxRecords = 10,
                IncludeMetadataHeader = true,
                IncludeTransferHistory = false
            }
        };

        var queryBatchResponse = await senderOwnerClient.V1.Drive.QueryBatch(request);
        var theFileResponse = queryBatchResponse.Content.SearchResults.SingleOrDefault();
        Assert.That(theFileResponse, Is.Not.Null);

        Assert.That(theFileResponse.ServerMetadata.TransferHistory, Is.Null);
    }

    [Test]
    public async Task GetModifiedOnSenderCanExcludeRecipientTransferHistory()
    {
        var senderOwnerClient = await LoginAsOwner(Identities.Frodo);
        var recipientOwnerClient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.Write;

        var targetDrive = TargetDrive.NewTargetDrive();
        await OutboxScenario.PrepareAsync(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

        const int fileType = 1033;
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new()
            {
                Content = UploadedContent,
                FileType = fileType,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.Connected
        };

        var storageOptions = new StorageOptions
        {
            Drive = targetDrive
        };

        var transitOptions = new TransitOptions
        {
            Recipients = [recipientOwnerClient.Identity.DomainName]
        };

        var (uploadResponse, _) = await senderOwnerClient.V1.Drive.UploadNewEncryptedMetadata(
            fileMetadata,
            storageOptions,
            transitOptions
        );

        Assert.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uploadResult = uploadResponse.Content;
        Assert.That(uploadResult.RecipientStatus.Count, Is.EqualTo(1));
        Assert.That(uploadResult.RecipientStatus[recipientOwnerClient.Identity], Is.EqualTo(TransferStatus.Enqueued));

        await senderOwnerClient.Sync.DrainOutboxAsync();

        // Assert: file that was sent has peer transfer status updated
        var getHistoryResponse = await senderOwnerClient.V1.Drive.GetTransferHistory(uploadResult.File);
        Assert.That(getHistoryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var theHistory = getHistoryResponse.Content;
        Assert.That(theHistory, Is.Not.Null);
        var recipientStatus = theHistory.GetHistoryItem(recipientOwnerClient.Identity);
        Assert.That(recipientStatus, Is.Not.Null, "There should be a status update for the recipient");
        Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.EqualTo(uploadResult.NewVersionTag));

        //Get modified to results ensure it will show up after a transfer
        var queryModifiedResponse = await senderOwnerClient.V1.Drive.QueryModified(new QueryModifiedRequest
        {
            QueryParams = new()
            {
                TargetDrive = targetDrive,
                FileType = [fileMetadata.AppData.FileType]
            },
            ResultOptions = new QueryModifiedResultOptions
            {
                MaxDate = UnixTimeUtc.Now().AddSeconds(+100).milliseconds,
                IncludeTransferHistory = false
            }
        });

        Assert.That(queryModifiedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var modifiedResults = queryModifiedResponse.Content;
        var fileInResults = modifiedResults.SearchResults.SingleOrDefault(r => r.FileId == uploadResult.File.FileId);
        Assert.That(fileInResults, Is.Not.Null);

        Assert.That(fileInResults.ServerMetadata.TransferHistory, Is.Null);
    }

    [Test, Ignore("How do i test this?  see notes in test")]
    public Task CanSetDependencyIdOnOutboxItem()
    {
        //TODO: how do i test this?  It seems it will require me to inject debug-test code to the outbox
        //processor to slow down processing enough to test the dependency ordering of files received
        return Task.CompletedTask;
    }

    /// <summary>
    /// The original's private <c>UploadEncryptedMetadata</c>. Kept local rather than folded into
    /// <see cref="PeerTransferScenario.UploadEncryptedMetadataAsync"/> because the first test compares
    /// the recipient's copy against the encrypted content the client produced, which the shared helper
    /// does not hand back.
    /// </summary>
    private static Task<(ApiResponse<UploadResult> response, string encryptedJsonContent64)>
        UploadEncryptedMetadataAsync(
            OwnerSession senderOwnerClient,
            TargetDrive targetDrive,
            TransitOptions transitOptions)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new()
            {
                Content = UploadedContent,
                FileType = default,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.Connected
        };

        var storageOptions = new StorageOptions
        {
            Drive = targetDrive
        };

        return senderOwnerClient.V1.Drive.UploadNewEncryptedMetadata(
            fileMetadata,
            storageOptions,
            transitOptions
        );
    }
}
