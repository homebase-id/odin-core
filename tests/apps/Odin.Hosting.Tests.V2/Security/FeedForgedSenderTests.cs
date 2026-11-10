using System;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Ported.DataSubscription;
using Odin.Services.Base;
using Odin.Services.DataSubscription;
using Odin.Services.DataSubscription.SendingHost;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Security;

/// <summary>
/// An identity the owner follows must not be able to replace another author's
/// item on the owner's feed drive by naming that item's global transit id.
/// </summary>
/// <remarks>
/// Frodo follows Sam and Merry. Sam posts, so the post is on Frodo's feed. Merry then plays a modified
/// server: the request is built here, because a stock server never sends it, and goes out through
/// Merry's own peer client to Frodo's real feed endpoint.
/// </remarks>
[TestFixture]
public class FeedForgedSenderTests : V2Fixture
{
    private const int FileType = 20871;
    private const string SamsContent = "Sam wrote this";
    private const string ForgedContent = "Merry forged this";

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry];

    [Test]
    public async Task FollowedIdentity_CannotReplaceAnotherAuthorsFeedItem_WithADirectUpdate()
    {
        var (frodo, sam, merry, gtid) = await ArrangeSamsPostOnFrodosFeedAsync();

        var response = await SendAsMerryAsync(merry, frodo, new UpdateFeedFileMetadataRequest
        {
            FileId = FeedFile(gtid),
            FileMetadata = ForgedMetadata(gtid, merry, isEncrypted: false),
            FeedDistroType = FeedDistroType.Normal
        });

        await AssertItemIsStillSamsAsync(frodo, sam, $"Merry's update was answered {response}");
    }

    [Test]
    public async Task FollowedIdentity_CannotReplaceAnotherAuthorsFeedItem_ByClaimingACollaborativeChannel()
    {
        var (frodo, sam, merry, gtid) = await ArrangeSamsPostOnFrodosFeedAsync();

        var response = await SendAsMerryAsync(merry, frodo, new UpdateFeedFileMetadataRequest
        {
            FileId = FeedFile(gtid),
            FileMetadata = ForgedMetadata(gtid, merry, isEncrypted: true),
            FeedDistroType = FeedDistroType.CollaborativeChannel,
            EncryptedPayload = await SealCollaborativeClaimAsync(merry, frodo)
        });

        await frodo.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        await AssertItemIsStillSamsAsync(frodo, sam, $"Merry's update was answered {response}");
    }

    private async Task<(OwnerSession Frodo, OwnerSession Sam, OwnerSession Merry, Guid Gtid)> ArrangeSamsPostOnFrodosFeedAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);

        var channel = await DataSubscriptionScenario.CreateChannelDriveAsync(sam, allowAnonymousReads: true);
        await DataSubscriptionScenario.FollowAsync(frodo, sam);
        await DataSubscriptionScenario.FollowAsync(frodo, merry);

        var upload = await DataSubscriptionScenario.UploadStandardUnencryptedFileToChannelAsync(sam, channel, SamsContent, FileType);
        await sam.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        var item = await DataSubscriptionScenario.AssertFeedDriveHasFileAsync(frodo,
            DataSubscriptionScenario.FeedQueryByGlobalTransitId(upload), SamsContent, upload);
        Assert.That(item.FileMetadata.SenderOdinId, Is.EqualTo(sam.Identity.DomainName), "arrange: the post should be Sam's");

        return (frodo, sam, merry, upload.GlobalTransitId.GetValueOrDefault());
    }

    private async Task<string> SendAsMerryAsync(OwnerSession merry, OwnerSession frodo, UpdateFeedFileMetadataRequest request)
    {
        await using var scope = Host.GetTenantScope(merry.Identity.DomainName).BeginLifetimeScope();
        var client = await scope.Resolve<IOdinHttpClientFactory>()
            .CreateClientAsync<IFeedDistributorHttpClient>(frodo.Identity, fileSystemType: FileSystemType.Standard);

        var response = await client.SendFeedFileMetadata(request);
        return response.StatusCode.ToString();
    }

    /// <summary>The payload only the caller vouches for: anyone can encrypt it to Frodo's public key.</summary>
    private async Task<EccEncryptedPayload> SealCollaborativeClaimAsync(OwnerSession merry, OwnerSession frodo)
    {
        var claim = new FeedItemPayload
        {
            KeyHeaderBytes = KeyHeader.NewRandom16().Combine().GetKey(),
            DriveOriginWasCollaborative = true
        };

        await using var scope = Host.GetTenantScope(merry.Identity.DomainName).BeginLifetimeScope();
        return await scope.Resolve<PublicPrivateKeyService>().EccEncryptPayloadForRecipientAsync(
            PublicPrivateKeyType.OfflineKey,
            frodo.Identity,
            OdinSystemSerializer.Serialize(claim).ToUtf8ByteArray());
    }

    private static GlobalTransitIdFileIdentifier FeedFile(Guid gtid) => new()
    {
        GlobalTransitId = gtid,
        TargetDrive = WellKnownAppDrives.FeedDrive
    };

    private static FileMetadata ForgedMetadata(Guid gtid, OwnerSession merry, bool isEncrypted) => new()
    {
        GlobalTransitId = gtid,
        FileState = FileState.Active,
        IsEncrypted = isEncrypted,
        AppData = new AppFileMetaData
        {
            Content = ForgedContent,
            FileType = FileType
        },
        DataSource = new DataSource
        {
            Identity = merry.Identity,
            DriveId = Guid.NewGuid(),
            PayloadsAreRemote = true
        },
        Payloads = []
    };

    private static async Task AssertItemIsStillSamsAsync(OwnerSession frodo, OwnerSession sam, string context)
    {
        var results = await DataSubscriptionScenario.QueryBatchAsync(frodo,
            Ported.Feed.FeedScenario.FeedQuery(FileType));
        Assert.That(results.Count, Is.EqualTo(1), context);

        var item = results.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.FileMetadata.AppData.Content, Is.EqualTo(SamsContent), context);
            Assert.That(item.FileMetadata.SenderOdinId, Is.EqualTo(sam.Identity.DomainName), context);
        });
    }
}
