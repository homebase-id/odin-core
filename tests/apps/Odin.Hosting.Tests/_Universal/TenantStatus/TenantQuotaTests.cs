using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Admin.Tenants;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;
using Status = Odin.Services.Registry.TenantStatus;
using static Odin.Hosting.Tests._Universal.TenantStatus.TenantStatusTestSupport;

namespace Odin.Hosting.Tests._Universal.TenantStatus;

/// <summary>
/// Out-of-quota enforcement: an identity over its quota refuses everything that adds payload bytes
/// (507 + Retry-After) and keeps doing everything else. Senders hold such an item in their outbox
/// without spending attempts, instead of dropping it after ~3.8h.
/// </summary>
public class TenantQuotaTests
{
    private WebScaffold _scaffold = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        var env = AdminEnv();
        _scaffold.RunBeforeAnyTests(envOverrides: env, testIdentities: [TestIdentities.Frodo, TestIdentities.Samwise]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [TearDown]
    public async Task ResetStatuses()
    {
        foreach (var identity in new[] { TestIdentities.Frodo, TestIdentities.Samwise })
        {
            await SetStatusAsync(identity.OdinId.DomainName, Status.Active);
        }
    }

    //
    // Owner and app writes
    //

    [Test]
    public async Task UploadWithAPayloadIsRefusedWhileOutOfQuota()
    {
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var targetDrive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await owner.DriveManager.CreateDrive(targetDrive, "quota drive", "", false)).IsSuccessStatusCode);

        await SetStatusAsync(owner.OdinId.DomainName, Status.OutOfQuota);

        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var response = await owner.DriveRedux.UploadNewFile(
            targetDrive,
            MetadataWith("upload with payload"),
            ManifestFor(payload),
            [payload]);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InsufficientStorage));
        Assert.That(response.Headers.RetryAfter?.Delta, Is.EqualTo(TimeSpan.FromSeconds(TenantStatusRules.OutOfQuotaRetryAfterSeconds)));
    }

    [Test]
    public async Task UploadOfAPayloadWithoutThumbnailsIsAlsoRefused()
    {
        // The thumbnail and the payload are separate parts of the upload; both have to be refused
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var targetDrive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await owner.DriveManager.CreateDrive(targetDrive, "quota drive", "", false)).IsSuccessStatusCode);

        await SetStatusAsync(owner.OdinId.DomainName, Status.OutOfQuota);

        var payload = new TestPayloadDefinition
        {
            Key = "no_thumbs1",
            ContentType = "text/plain",
            Content = "a payload with no thumbnails".ToUtf8ByteArray(),
            Thumbnails = new List<ThumbnailContent>()
        };

        var response = await owner.DriveRedux.UploadNewFile(
            targetDrive,
            MetadataWith("payload without thumbnails"),
            ManifestFor(payload),
            [payload]);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InsufficientStorage));
    }

    [Test]
    public async Task MetadataOnlyWritesKeepWorkingWhileOutOfQuota()
    {
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var targetDrive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await owner.DriveManager.CreateDrive(targetDrive, "quota drive", "", false)).IsSuccessStatusCode);

        await SetStatusAsync(owner.OdinId.DomainName, Status.OutOfQuota);

        var upload = await owner.DriveRedux.UploadNewMetadata(targetDrive, MetadataWith("no payload here"));
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a file without payloads is still accepted");

        // ... and so is editing it, and deleting it
        var edit = await owner.DriveRedux.UpdateExistingMetadata(upload.Content!.File, upload.Content.NewVersionTag,
            MetadataWith("edited while out of quota"));
        Assert.That(edit.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var delete = await owner.DriveRedux.SoftDeleteFile(upload.Content.File);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task AddingAPayloadToAnExistingFileIsRefusedWhileOutOfQuota()
    {
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var targetDrive = TargetDrive.NewTargetDrive();
        ClassicAssert.IsTrue((await owner.DriveManager.CreateDrive(targetDrive, "quota drive", "", false)).IsSuccessStatusCode);

        var upload = await owner.DriveRedux.UploadNewMetadata(targetDrive, MetadataWith("file to attach to"));
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await SetStatusAsync(owner.OdinId.DomainName, Status.OutOfQuota);

        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var response = await owner.DriveRedux.UploadPayloads(
            upload.Content!.File,
            upload.Content.NewVersionTag,
            ManifestFor(payload),
            [payload]);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InsufficientStorage));
        Assert.That(response.Headers.RetryAfter?.Delta, Is.EqualTo(TimeSpan.FromSeconds(TenantStatusRules.OutOfQuotaRetryAfterSeconds)));
    }

    //
    // Peers
    //

    [Test]
    public async Task TransferWithoutAPayloadIsDeliveredToAnOutOfQuotaRecipient()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenarioAsync(sender, recipient, targetDrive);

        try
        {
            await SetStatusAsync(recipient.OdinId.DomainName, Status.OutOfQuota);

            var uploadResult = await SendMetadataOnlyAsync(sender, targetDrive, recipient.OdinId);

            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive, TimeSpan.FromSeconds(60));
            await recipient.DriveRedux.ProcessInbox(targetDrive);

            var received = await recipient.DriveRedux.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
            Assert.That(received.Content.SearchResults.Count(), Is.EqualTo(1), "a file without payloads still arrives");
        }
        finally
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }
    }

    [Test]
    public async Task TransferWithAPayloadWaitsForTheRecipientToHaveRoomAgain()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenarioAsync(sender, recipient, targetDrive);

        try
        {
            await SetStatusAsync(recipient.OdinId.DomainName, Status.OutOfQuota);

            var uploadResult = await SendWithPayloadAsync(sender, targetDrive, recipient.OdinId);

            // The recipient answered 507 + Retry-After, so the item is deferred rather than retried
            await WaitUntilAsync(async () => (await ReadOutboxAsync(_scaffold, sender.Identity, targetDrive))
                .Any(r => r.nextRunTime.milliseconds > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60 * 1000),
                "the sender to defer the item");

            var deferred = await ReadOutboxAsync(_scaffold, sender.Identity, targetDrive);
            Assert.That(deferred, Has.Count.EqualTo(1));
            Assert.That(deferred[0].checkOutCount, Is.EqualTo(0), "a retry-later must not spend an attempt");

            var expected = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + TenantStatusRules.OutOfQuotaRetryAfterSeconds * 1000;
            Assert.That(deferred[0].nextRunTime.milliseconds, Is.EqualTo(expected).Within((long)TimeSpan.FromMinutes(2).TotalMilliseconds),
                "the sender waits roughly the hour it was asked for");

            var history = await sender.DriveRedux.GetTransferHistory(uploadResult.File);
            var item = history.Content?.GetHistoryItem(recipient.OdinId);
            Assert.That(item?.IsInOutbox, Is.True, "the file still shows as pending delivery");

            // Room again: the item is delivered, payload and all
            await SetStatusAsync(recipient.OdinId.DomainName, Status.Active);
            await DrainOutboxAsync(_scaffold, sender.Identity);
            await recipient.DriveRedux.ProcessInbox(targetDrive);

            var received = await recipient.DriveRedux.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
            var file = received.Content.SearchResults.SingleOrDefault();
            Assert.That(file, Is.Not.Null, "recipient has the file once it has room");
            Assert.That(file!.FileMetadata.Payloads.Count, Is.EqualTo(1), "and its payload");
        }
        finally
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }
    }

    [Test]
    public async Task APausedRecipientAlsoDefersTheItemWithoutSpendingAttempts()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenarioAsync(sender, recipient, targetDrive);

        try
        {
            await SetStatusAsync(recipient.OdinId.DomainName, Status.Paused);

            var uploadResult = await SendMetadataOnlyAsync(sender, targetDrive, recipient.OdinId);

            await WaitUntilAsync(async () => (await ReadOutboxAsync(_scaffold, sender.Identity, targetDrive))
                .Any(r => r.nextRunTime.milliseconds > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60 * 1000),
                "the sender to defer the item");

            var deferred = await ReadOutboxAsync(_scaffold, sender.Identity, targetDrive);
            Assert.That(deferred, Has.Count.EqualTo(1));
            Assert.That(deferred[0].checkOutCount, Is.EqualTo(0), "a paused recipient must not cost attempts");

            var expected = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + TenantStatusRules.PausedRetryAfterSeconds * 1000;
            Assert.That(deferred[0].nextRunTime.milliseconds, Is.EqualTo(expected).Within((long)TimeSpan.FromMinutes(2).TotalMilliseconds),
                "the sender waits the ten minutes a paused identity asks for");

            await SetStatusAsync(recipient.OdinId.DomainName, Status.Active);
            await DrainOutboxAsync(_scaffold, sender.Identity);
            await recipient.DriveRedux.ProcessInbox(targetDrive);

            var received = await recipient.DriveRedux.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
            Assert.That(received.Content.SearchResults.Count(), Is.EqualTo(1), "delivered once the identity is back");
        }
        finally
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }
    }

    //
    // Helpers
    //

    private static UploadFileMetadata MetadataWith(string content)
    {
        return new UploadFileMetadata
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new UploadAppFileMetaData { Content = content },
            AccessControlList = AccessControlList.OwnerOnly
        };
    }

    private static async Task<UploadResult> SendMetadataOnlyAsync(OwnerApiClientRedux sender, TargetDrive targetDrive, OdinId recipient)
    {
        var metadata = MetadataWith("chat without attachments");
        metadata.AllowDistribution = true;
        metadata.AccessControlList = AccessControlList.Connected;

        var response = await sender.DriveRedux.UploadNewFile(targetDrive, metadata, new UploadManifest(), [],
            new TransitOptions { Recipients = [recipient] });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.RecipientStatus[recipient], Is.EqualTo(TransferStatus.Enqueued));
        return response.Content;
    }

    private static async Task<UploadResult> SendWithPayloadAsync(OwnerApiClientRedux sender, TargetDrive targetDrive, OdinId recipient)
    {
        var metadata = MetadataWith("chat with an attachment");
        metadata.AllowDistribution = true;
        metadata.AccessControlList = AccessControlList.Connected;

        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var response = await sender.DriveRedux.UploadNewFile(
            targetDrive,
            metadata,
            ManifestFor(payload),
            [payload],
            new TransitOptions { Recipients = [recipient] });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content!.RecipientStatus[recipient], Is.EqualTo(TransferStatus.Enqueued));
        return response.Content;
    }
}
