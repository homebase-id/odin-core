using System.Collections.Generic;
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

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Outbox/OutboxProcessingTestsMultipleRecipients.cs
///
/// One send fanned out to two connected recipients: the sender's transfer history records each leg,
/// and <c>QueryModified</c> surfaces the file afterwards while still being able to leave the
/// per-recipient history out of its results.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>No caller matrix in the original and none here.</item>
/// <item><c>RecipientTransferHistoryOnSenderIsUpdatedWhenTransferringFile</c> keeps its
/// <c>[Ignore]("Timing issue when running tests; need to fix the test; system is fine")</c> verbatim.
/// Worth knowing when someone comes back to it: the timing it complains about was the background
/// outbox processor, which this framework does not use — the drain here is synchronous.</item>
/// <item><c>WaitForEmptyOutbox</c> becomes <c>Sync.DrainOutboxAsync()</c> — the V1 call is a passive
/// poll on the outbox background service, which the fast host registers but never starts.</item>
/// <item>The original's <c>PrepareScenario</c> sent the connection request before the recipient had
/// created its drive and circle; <see cref="OutboxScenario.PrepareAsync"/> creates both first. Same
/// end state, and nothing asserts on the intermediate one.</item>
/// <item>Carried defect: both tests re-assert the single <c>uploadResponse</c> — status code,
/// recipient count, per-recipient status — once per recipient inside the <c>foreach</c>, which only
/// the <c>RecipientStatus[recipient]</c> lookup varies with. Left as written.</item>
/// <item>Trailing <c>DeleteScenario</c> calls were cleanup only and are dropped — per-test reset
/// covers them.</item>
/// </list>
/// </remarks>
[TestFixture]
public class OutboxProcessingTestsMultipleRecipients : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Pippin, Identities.Sam];

    [Test]
    [Ignore("Timing issue when running tests; need to fix the test; system is fine")]
    public async Task RecipientTransferHistoryOnSenderIsUpdatedWhenTransferringFile()
    {
        var senderOwnerClient = await LoginAsOwner(Identities.Frodo);

        var sam = await LoginAsOwner(Identities.Sam);
        var pippin = await LoginAsOwner(Identities.Pippin);

        List<OwnerSession> recipients = [sam, pippin];

        const DrivePermission drivePermissions = DrivePermission.Write;

        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenarioAsync(senderOwnerClient, recipients, targetDrive, drivePermissions);

        const string uploadedContent = "pie";

        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new()
            {
                Content = uploadedContent,
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

        var transitOptions = new TransitOptions
        {
            Recipients = recipients.Select(r => r.Identity.DomainName).ToList()
        };

        var (uploadResponse, _) = await senderOwnerClient.V1.Drive.UploadNewEncryptedMetadata(
            fileMetadata,
            storageOptions,
            transitOptions
        );

        await senderOwnerClient.Sync.DrainOutboxAsync();

        foreach (var recipient in recipients)
        {
            Assert.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var uploadResult = uploadResponse.Content;
            Assert.That(uploadResult.RecipientStatus.Count, Is.EqualTo(recipients.Count));
            Assert.That(uploadResult.RecipientStatus[recipient.Identity], Is.EqualTo(TransferStatus.Enqueued));

            // Assert: file that was sent has peer transfer status updated
            var getHistoryResponse = await senderOwnerClient.V1.Drive.GetTransferHistory(uploadResult.File);
            Assert.That(getHistoryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var theHistory = getHistoryResponse.Content;
            Assert.That(theHistory, Is.Not.Null);
            var recipientStatus = theHistory.GetHistoryItem(recipient.Identity);
            Assert.That(recipientStatus, Is.Not.Null, "There should be a status update for the recipient");
            Assert.That(recipientStatus.IsInOutbox, Is.False);
            Assert.That(recipientStatus.IsReadByRecipient, Is.False);
            Assert.That(recipientStatus.LatestSuccessfullyDeliveredVersionTag, Is.EqualTo(uploadResult.NewVersionTag));
        }
    }

    [Test]
    public async Task GetModifiedOfSenderFilesIncludesFilesWithUpdatedPeerTransferStatusAndCanExcludeRecipientTransferHistory()
    {
        var senderOwnerClient = await LoginAsOwner(Identities.Frodo);

        var sam = await LoginAsOwner(Identities.Sam);
        var pippin = await LoginAsOwner(Identities.Pippin);

        List<OwnerSession> recipients = [sam, pippin];

        const DrivePermission drivePermissions = DrivePermission.Write;

        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenarioAsync(senderOwnerClient, recipients, targetDrive, drivePermissions);

        const string uploadedContent = "pie";

        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = 1011,
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
            Recipients = recipients.Select(r => r.Identity.DomainName).ToList()
        };

        var (uploadResponse, _) = await senderOwnerClient.V1.Drive.UploadNewEncryptedMetadata(
            fileMetadata,
            storageOptions,
            transitOptions
        );

        await senderOwnerClient.Sync.DrainOutboxAsync();

        foreach (var recipient in recipients)
        {
            Assert.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var uploadResult = uploadResponse.Content;
            Assert.That(uploadResult.RecipientStatus.Count, Is.EqualTo(recipients.Count));
            Assert.That(uploadResult.RecipientStatus[recipient.Identity], Is.EqualTo(TransferStatus.Enqueued));

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
            Assert.That(fileInResults, Is.Not.Null, $"recipient: {recipient.Identity}");

            Assert.That(fileInResults.ServerMetadata.TransferHistory, Is.Null, $"recipient: {recipient.Identity}");
        }
    }

    private static async Task PrepareScenarioAsync(
        OwnerSession senderOwnerClient,
        List<OwnerSession> recipients,
        TargetDrive targetDrive,
        DrivePermission drivePermissions)
    {
        foreach (var recipient in recipients)
        {
            await OutboxScenario.PrepareAsync(senderOwnerClient, recipient, targetDrive, drivePermissions);
        }
    }
}
