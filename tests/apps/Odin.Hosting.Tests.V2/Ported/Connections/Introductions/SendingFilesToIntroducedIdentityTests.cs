#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using static Odin.Hosting.Tests.V2.Ported.Connections.Introductions.IntroductionTestUtils;

namespace Odin.Hosting.Tests.V2.Ported.Connections.Introductions;

/// <summary>
/// Port of <c>_Universal/Owner/Connections/Introductions/SendingFilesToIntroducedIdentityTests</c>.
/// The auto-connection an introduction produces is a real connection: the Auto-connected circle it
/// grants carries Write on the chat drive, so one introducee can send the other a file without any
/// further setup.
/// </summary>
/// <remarks>
/// <para>
/// Three passive polls become explicit drains: the introducer's
/// <c>WaitForEmptyOutbox(TransientTempDrive)</c> and each introducee's
/// <c>AwaitIntroductionsProcessing()</c> (the same poll under a different name) become
/// <c>Sync.DrainOutboxAsync()</c>, and the post-upload <c>WaitForEmptyOutbox(chatDrive)</c> plus
/// <c>ProcessInbox(chatDrive)</c> become <c>PeerFlow.DistributeAsync</c>. Draining an introducee's
/// outbox is what sends the introductory connection request, so this is behaviour-preserving, not
/// merely a timing change.
/// </para>
/// <para>
/// The chat drive is not created here: <c>V2Fixture</c>'s warm-up runs the tenant initial-setup
/// flow, which creates the well-known app drives, exactly as <c>WebScaffold</c>'s did.
/// </para>
/// <para>
/// The trailing <c>Cleanup()</c> (delete introductions, disconnect every pairing, delete stray
/// requests, unblock) was lifecycle only and asserted nothing; per-test reset owns it. The
/// <c>DeleteAllIntroductions</c> calls inside <c>Prepare</c> are kept as arrange, as
/// <see cref="IntroductionTestUtils.PrepareIntroducerAndClearIntroductionsAsync"/>.
/// <c>SetupCallerWithOwner</c> is not in play — no caller matrix, <c>LoginAsOwner</c> only.
/// </para>
/// </remarks>
[TestFixture]
public class SendingFilesToIntroducedIdentityTests : V2Fixture
{
    /// <summary>These tests drive introductions that are expected to fail delivery.</summary>
    protected override IReadOnlyCollection<string> ToleratedErrorLogSubstrings =>
        [OutboxDeliveryFailureLogged];

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Merry, Identities.Sam];

    [Test]
    public async Task CanSendFilesOnChatDriveToIntroducedIdentity()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        await PrepareIntroducerAndClearIntroductionsAsync(frodo, sam, merry);

        var response = await Requests(frodo).SendIntroductions(new IntroductionGroup
        {
            Message = "test message from frodo",
            Recipients = [sam.Identity, merry.Identity]
        });

        var introResult = response.Content!;
        Assert.That(introResult.RecipientStatus[sam.Identity], Is.True);
        Assert.That(introResult.RecipientStatus[merry.Identity], Is.True);

        await frodo.Sync.DrainOutboxAsync();

        // There are background processes running which will send introductions automatically
        // we can also call an endpoint to force this.
        // since we don't know when this will occur, we'll call the endpoint

        // there's also logic dictating that when sending a connection request due to an
        // introduction, we do not send it if there's already an incoming request

        // so - we have to add some logic into this test

        await sam.Sync.DrainOutboxAsync();
        await merry.Sync.DrainOutboxAsync();

        //
        // Act - now that merry and sam are introduced; send a file from sam to merry
        //
        var targetDrive = WellKnownAppDrives.ChatDrive;
        var fileMetadata = SampleMetadataData.CreateWithContent(fileType: 333, "sam says hi to merry", AccessControlList.Connected);
        fileMetadata.AllowDistribution = true;

        var storage = new StorageOptions
        {
            Drive = targetDrive
        };
        var transitOptions = new TransitOptions
        {
            Recipients = [merry.Identity],
            Priority = OutboxPriority.High
        };

        var (sendFileToMerryResponse, _) = await sam.V1.Drive
            .UploadNewEncryptedMetadata(fileMetadata, storage, transitOptions);

        //
        // Assert - sam should have sent the file and merry should have it
        //
        Assert.That(sendFileToMerryResponse.IsSuccessStatusCode, Is.True);
        var samUploadResult = sendFileToMerryResponse.Content!;
        Assert.That(samUploadResult.RecipientStatus[merry.Identity], Is.EqualTo(TransferStatus.Enqueued));

        await Odin.Hosting.Tests.V2.Peer.PeerFlow.DistributeAsync(sam, merry, targetDrive);

        var getFileOnMerryResponse = await merry.V1.Drive
            .QueryByGlobalTransitId(samUploadResult.GlobalTransitIdFileIdentifier);

        Assert.That(getFileOnMerryResponse.IsSuccessStatusCode, Is.True);
        var fileOnMerry = getFileOnMerryResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(fileOnMerry, Is.Not.Null);
    }
}
