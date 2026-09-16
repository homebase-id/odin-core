using System;
using System.Collections.Generic;
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

namespace Odin.Hosting.Tests._Universal.TenantStatus;

/// <summary>
/// A recipient that keeps saying "retry later" does not hold an item forever: the sender gives up once
/// the item is older than Host:OutboxRetryLaterMaxAgeSeconds (7 days in production, 1 second here).
/// </summary>
public class OutboxRetryLaterMaxAgeTests
{
    private const string AdminApiKey = "your-secret-api-key-here";
    private const string AdminHost = "admin.dotyou.cloud:4444";

    private WebScaffold _scaffold = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        var env = new Dictionary<string, string>
        {
            { "Admin__ApiEnabled", "true" },
            { "Admin__ApiKey", AdminApiKey },
            { "Admin__ApiKeyHttpHeaderName", "Odin-Admin-Api-Key" },
            { "Admin__ApiPort", "4444" },
            { "Admin__Domain", "admin.dotyou.cloud" },
            { "Host__OutboxRetryLaterMaxAgeSeconds", "1" },
        };
        _scaffold.RunBeforeAnyTests(envOverrides: env, testIdentities: [TestIdentities.Frodo, TestIdentities.Samwise]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [Test]
    public async Task ItemIsDroppedOnceItHasBeenDeferredForLongerThanTheMaxAge()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenarioAsync(sender, recipient, targetDrive);

        try
        {
            await SetStatusViaAdminAsync(recipient.OdinId.DomainName, Status.OutOfQuota, null);

            var metadata = new UploadFileMetadata
            {
                AllowDistribution = true,
                IsEncrypted = false,
                AppData = new UploadAppFileMetaData { Content = "attachment for a full recipient" },
                AccessControlList = AccessControlList.Connected
            };

            var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
            var upload = await sender.DriveRedux.UploadNewFile(
                targetDrive,
                metadata,
                new UploadManifest { PayloadDescriptors = new List<TestPayloadDefinition> { payload }.ToPayloadDescriptorList().ToList() },
                [payload],
                new TransitOptions { Recipients = [recipient.OdinId] });

            Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(upload.Content!.RecipientStatus[recipient.OdinId], Is.EqualTo(TransferStatus.Enqueued));

            // The first attempt defers the item rather than dropping it
            await Task.Delay(TimeSpan.FromSeconds(2));
            Assert.That(await ReadOutboxAsync(sender.Identity), Has.Count.EqualTo(1), "still queued after the first refusal");

            // By the next attempt the item is past the deadline, so the sender gives up
            await DrainOutboxAsync(sender.Identity);

            Assert.That(await ReadOutboxAsync(sender.Identity), Is.Empty, "the item is dropped once it is too old");

            var history = await sender.DriveRedux.GetTransferHistory(upload.Content.File);
            var item = history.Content?.GetHistoryItem(recipient.OdinId);
            Assert.That(item, Is.Not.Null);
            Assert.That(item!.IsInOutbox, Is.False, "and the file no longer shows as pending");
            Assert.That(item.LatestTransferStatus, Is.EqualTo(LatestTransferStatus.SendingServerTooManyAttempts));
        }
        finally
        {
            await SetStatusViaAdminAsync(recipient.OdinId.DomainName, Status.Active, null);
            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }
    }

    //
    // Helpers
    //

    private static async Task<HttpResponseMessage> SetStatusViaAdminAsync(string domain, Status status, DisabledReason? reason)
    {
        var client = WebScaffold.HttpClientFactory.CreateClient(AdminHost);
        var json = OdinSystemSerializer.Serialize(new SetTenantStatusRequest { Status = status, DisabledReason = reason });
        var request = new HttpRequestMessage(HttpMethod.Patch, $"https://{AdminHost}/api/admin/v1/tenants/{domain}/status")
        {
            Headers = { { "Odin-Admin-Api-Key", AdminApiKey } },
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"setting {domain} to {status}");
        return response;
    }

    private async Task<List<OutboxRecord>> ReadOutboxAsync(TestIdentity identity)
    {
        var container = _scaffold.Services.GetRequiredService<IMultiTenantContainer>();
        await using var scope = container.GetTenantScope(identity.OdinId.DomainName).BeginLifetimeScope("RetryLaterMaxAgeTests");
        var (records, _) = await scope.Resolve<Odin.Core.Storage.Database.Identity.IdentityDatabase>().Outbox.PagingByRowIdAsync(100, null);
        return records;
    }

    private async Task DrainOutboxAsync(TestIdentity identity)
    {
        var container = _scaffold.Services.GetRequiredService<IMultiTenantContainer>();
        await using var scope = container.GetTenantScope(identity.OdinId.DomainName).BeginLifetimeScope("RetryLaterMaxAgeTests:drain");
        await scope.Resolve<PeerOutbox>().BringForwardScheduledItemsAsync();
        await scope.Resolve<PeerOutboxProcessorBackgroundService>().DrainAsync();
    }

    private static async Task PrepareScenarioAsync(OwnerApiClientRedux sender, OwnerApiClientRedux recipient, TargetDrive targetDrive)
    {
        ClassicAssert.IsTrue((await recipient.DriveManager.CreateDrive(targetDrive, "Target drive on recipient", "", false, false, false))
            .IsSuccessStatusCode);
        ClassicAssert.IsTrue((await sender.DriveManager.CreateDrive(targetDrive, "Target drive on sender", "", false, false, false))
            .IsSuccessStatusCode);

        var circleId = Guid.NewGuid();
        var createCircle = await recipient.Network.CreateCircle(circleId, "Circle with drive access", new PermissionSetGrantRequest
        {
            Drives = [new DriveGrantRequest { PermissionedDrive = new PermissionedDrive { Drive = targetDrive, Permission = DrivePermission.Write } }]
        });
        ClassicAssert.IsTrue(createCircle.IsSuccessStatusCode);

        await sender.Connections.SendConnectionRequest(recipient.OdinId, new List<GuidId>());
        await recipient.Connections.AcceptConnectionRequest(sender.OdinId, new List<GuidId> { circleId });
    }
}
