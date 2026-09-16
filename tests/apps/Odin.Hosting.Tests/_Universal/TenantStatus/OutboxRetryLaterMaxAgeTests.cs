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
using static Odin.Hosting.Tests._Universal.TenantStatus.TenantStatusTestSupport;

namespace Odin.Hosting.Tests._Universal.TenantStatus;

/// <summary>
/// A recipient that keeps saying "retry later" does not hold an item forever: the sender gives up once
/// the item is older than Host:OutboxRetryLaterMaxAgeSeconds (7 days in production, 1 second here).
/// </summary>
public class OutboxRetryLaterMaxAgeTests
{
    private WebScaffold _scaffold = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        var env = AdminEnv();
        env["Host__OutboxRetryLaterMaxAgeSeconds"] = "1";
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
            await SetStatusAsync(recipient.OdinId.DomainName, Status.OutOfQuota);

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
                ManifestFor(payload),
                [payload],
                new TransitOptions { Recipients = [recipient.OdinId] });

            Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(upload.Content!.RecipientStatus[recipient.OdinId], Is.EqualTo(TransferStatus.Enqueued));

            // The first attempt defers the item rather than dropping it
            await DrainOutboxAsync(_scaffold, sender.Identity);
            Assert.That(await ReadOutboxAsync(_scaffold, sender.Identity, targetDrive), Has.Count.EqualTo(1), "still queued after the first refusal");

            // Once it is older than the max age (1s here), the next attempt gives up
            await Task.Delay(TimeSpan.FromMilliseconds(1100));
            await DrainOutboxAsync(_scaffold, sender.Identity);

            Assert.That(await ReadOutboxAsync(_scaffold, sender.Identity, targetDrive), Is.Empty, "the item is dropped once it is too old");

            var history = await sender.DriveRedux.GetTransferHistory(upload.Content.File);
            var item = history.Content?.GetHistoryItem(recipient.OdinId);
            Assert.That(item, Is.Not.Null);
            Assert.That(item!.IsInOutbox, Is.False, "and the file no longer shows as pending");
            Assert.That(item.LatestTransferStatus, Is.EqualTo(LatestTransferStatus.SendingServerTooManyAttempts));
        }
        finally
        {
            await SetStatusAsync(recipient.OdinId.DomainName, Status.Active);
            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }
    }
}
