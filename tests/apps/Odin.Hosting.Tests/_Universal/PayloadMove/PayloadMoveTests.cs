using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Configuration;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.JobManagement;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Registry;
using Odin.Services.Registry.PayloadMove;
using Odin.Services.Tenant.Container;
using static Odin.Hosting.Tests._Universal.TenantStatus.TenantStatusTestSupport;
using Status = Odin.Services.Registry.TenantStatus;

namespace Odin.Hosting.Tests._Universal.PayloadMove;

/// <summary>
/// A payload move on a real host. The source side: the endpoint on the provisioning domain serves a paused
/// identity's payloads to whoever redeemed its handoff token, and nothing else to anybody. The target side:
/// a payload that has not arrived yet is a 404 nobody caches.
/// </summary>
public class PayloadMoveTests
{
    private const string ProvisioningHost = "provisioning.dotyou.cloud";
    private WebScaffold _scaffold = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        var env = AdminEnv();
        env["PayloadMove__SourceEnabled"] = "true";
        _scaffold.RunBeforeAnyTests(envOverrides: env, testIdentities: [TestIdentities.Frodo, TestIdentities.Samwise]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [TearDown]
    public async Task ResetStatus()
    {
        await SetStatusAsync(TestIdentities.Frodo.OdinId.DomainName, Status.Active);
    }

    [Test]
    public async Task ServesAPausedIdentitysPayloadsAndThumbnailsToTheRedeemer()
    {
        var (file, payload, _) = await UploadWithDriveAsync();
        await SetStatusAsync(TestIdentities.Frodo.OdinId.DomainName, Status.Paused);
        var credential = await RedeemAsync(await MintAsync());

        var get = await SendAsync(HttpMethod.Get, PayloadPath(file, payload), credential);
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.OK), await get.Content.ReadAsStringAsync());
        Assert.That(await get.Content.ReadAsByteArrayAsync(), Is.EqualTo(SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1().Content));

        var head = await SendAsync(HttpMethod.Head, PayloadPath(file, payload), credential);
        Assert.That(head.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(head.Content.Headers.ContentLength, Is.EqualTo(payload.BytesWritten));

        // What accept-missing asks before giving an object up (#1868): an object the source lacks is a 404
        var absent = new PayloadObject(file.driveId, file.fileId, payload.Key, new UnixTimeUtcUnique(payload.Uid.uniqueTime + 1), 0);
        var headAbsent = await SendAsync(HttpMethod.Head, absent.SourcePath(file.identityId), credential);
        Assert.That(headAbsent.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var thumbnail = payload.Thumbnails.Single();
        var thumb = await SendAsync(HttpMethod.Get, ThumbnailPath(file, payload, thumbnail), credential);
        Assert.That(thumb.StatusCode, Is.EqualTo(HttpStatusCode.OK), await thumb.Content.ReadAsStringAsync());
        Assert.That(await thumb.Content.ReadAsByteArrayAsync(), Is.EqualTo(TestMedia.ThumbnailBytes200));
    }

    [Test]
    public async Task RefusesEverythingElseWithA404()
    {
        var (file, payload, _) = await UploadWithDriveAsync();
        var domain = TestIdentities.Frodo.OdinId.DomainName;
        await SetStatusAsync(domain, Status.Paused);
        var token = await MintAsync();
        var credential = await RedeemAsync(token);
        var path = PayloadPath(file, payload);

        Assert.That((await RedeemRawAsync(token)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "a second redemption");
        Assert.That((await SendAsync(HttpMethod.Get, path, "wrong")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "a wrong credential");
        Assert.That((await SendAsync(HttpMethod.Get, path, null)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "no credential");
        Assert.That((await SendAsync(HttpMethod.Get, path.Replace(file.identityId.ToString(), IdOf(TestIdentities.Samwise).ToString()), credential)).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound), "another identity");
        Assert.That((await SendAsync(HttpMethod.Get, path.Replace($"/{payload.Uid.uniqueTime}", "/12345"), credential)).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound), "an object that does not exist");
        Assert.That((await SendAsync(HttpMethod.Get, path.Replace($"/{payload.Key}/", "/BAD..KEY/"), credential)).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound), "a malformed key");
        // The paused identity's own host answers 503 before any controller runs
        Assert.That((await SendAsync(HttpMethod.Get, path, credential, host: domain)).StatusCode,
            Is.Not.EqualTo(HttpStatusCode.OK), "on the identity's own host rather than the provisioning domain");

        await SetStatusAsync(domain, Status.Active);
        Assert.That((await SendAsync(HttpMethod.Get, path, credential)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "an active identity is never served");
    }

    [Test]
    public async Task TheSourceCannotBeDeletedUntilTheTargetReportsComplete()
    {
        await SetStatusAsync(TestIdentities.Frodo.OdinId.DomainName, Status.Paused);
        var credential = await RedeemAsync(await MintAsync());

        var registry = _scaffold.Services.GetRequiredService<IIdentityRegistry>();
        var refused = Assert.ThrowsAsync<OdinClientException>(() => registry.DeleteRegistration(TestIdentities.Frodo.OdinId.DomainName));
        Assert.That(refused!.Message, Does.Contain("payloads have not all reached the target"));

        var complete = await SendAsync(HttpMethod.Post, $"{Root()}/complete", credential);
        Assert.That(complete.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await Source().IsTransferPendingAsync(IdOf(TestIdentities.Frodo)), Is.False);
        Assert.That((await SendAsync(HttpMethod.Post, $"{Root()}/complete", credential)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "completion revokes the credential");
        // Not deleting frodo: the fixture shares it
    }

    [Test]
    public async Task APayloadNotHereYetIsAnUncacheable404WhileAMoveIsBringingIt()
    {
        var (file, payload, targetDrive) = await UploadWithDriveAsync();
        var identityId = IdOf(TestIdentities.Frodo);
        var config = _scaffold.Services.GetRequiredService<OdinConfiguration>();
        // Through the identity's own store: on disk here, on S3 under RUN_S3_TESTS
        await using (var scope = TenantScope(TestIdentities.Frodo))
        {
            await scope.Resolve<LongTermPayloadStore>().DeleteAsync(new TenantPathManager(config, identityId)
                .GetPayloadDirectoryAndFileName(file.driveId, file.fileId, payload.Key, payload.Uid));
        }
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var fileId = new ExternalFileIdentifier { FileId = file.fileId, TargetDrive = targetDrive };

        // No move: a missing payload is what it always was, a server error
        var withoutMove = await owner.DriveRedux.GetPayload(fileId, payload.Key);
        Assert.That(withoutMove.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        _scaffold.ClearLogEvents();

        // A move bringing it (its source unreachable, so it stays unfinished)
        var jobManager = _scaffold.Services.GetRequiredService<IJobManager>();
        await PayloadMoveJob.ScheduleAsync(jobManager, identityId, "https://127.0.0.1:1", "token", file.rowId);
        try
        {
            var duringMove = await owner.DriveRedux.GetPayload(fileId, payload.Key);
            Assert.That(duringMove.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(duringMove.Headers.CacheControl?.NoStore, Is.True, duringMove.Headers.ToString());
            Assert.That(duringMove.Headers.RetryAfter?.Delta, Is.EqualTo(TimeSpan.FromMinutes(1)));
        }
        finally
        {
            await jobManager.DeleteJobByHashAsync(PayloadMoveJob.JobHashFor(identityId));
        }
    }

    [Test]
    public async Task TheAdminApiShowsBothSidesWithoutTheSecrets()
    {
        var domain = TestIdentities.Frodo.OdinId.DomainName;
        var identityId = IdOf(TestIdentities.Frodo);
        var jobManager = _scaffold.Services.GetRequiredService<IJobManager>();

        var none = await SendAdminAsync(HttpMethod.Get, $"tenants/{TestIdentities.Samwise.OdinId.DomainName}/payload-move");
        var noneReport = OdinSystemSerializer.Deserialize<PayloadMoveReport>(await none.Content.ReadAsStringAsync())!;
        Assert.That(noneReport.Source, Is.Null);
        Assert.That(noneReport.Target, Is.Null);

        await MintAsync(); // as the source
        await PayloadMoveJob.ScheduleAsync(jobManager, identityId, "https://127.0.0.1:1", "the-secret-token", 7); // and the target
        try
        {
            var response = await SendAdminAsync(HttpMethod.Get, $"tenants/{domain}/payload-move");
            var json = await response.Content.ReadAsStringAsync();
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), json);
            Assert.That(json, Does.Not.Contain("the-secret-token"));

            var report = OdinSystemSerializer.Deserialize<PayloadMoveReport>(json)!;
            Assert.That(report.Source!.Pending, Is.True);
            Assert.That(report.Source.RedeemedAt, Is.Null);
            Assert.That(report.Target!.Progress.StartRowId, Is.EqualTo(7));
            Assert.That(report.Target.Progress.BaseUrl, Is.EqualTo("https://127.0.0.1:1"));

            // #1868: only a move that ended with nothing but objects the source lacks can give them up
            var accept = await SendAdminAsync(HttpMethod.Post, $"tenants/{domain}/payload-move/accept-missing");
            var acceptBody = await accept.Content.ReadAsStringAsync();
            Assert.That(accept.StatusCode, Is.AnyOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict),
                $"conflict only if the runner holds it right now: {acceptBody}");
            if (accept.StatusCode == HttpStatusCode.BadRequest)
            {
                Assert.That(acceptBody, Does.Contain("not CompleteWithFailures"));
            }

            var retry = await SendAdminAsync(HttpMethod.Post, $"tenants/{domain}/payload-move/retry");
            Assert.That(retry.StatusCode, Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.Conflict), "conflict only if the runner holds it right now");
        }
        finally
        {
            await jobManager.DeleteJobByHashAsync(PayloadMoveJob.JobHashFor(identityId));
        }

        var noJob = await SendAdminAsync(HttpMethod.Post, $"tenants/{domain}/payload-move/retry");
        Assert.That(noJob.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var noJobAccept = await SendAdminAsync(HttpMethod.Post, $"tenants/{domain}/payload-move/accept-missing");
        Assert.That(noJobAccept.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // #1871: an Inbox item a peer sent with payloads is not in the drive index until it is processed. What the
    // move fetches for it must be exactly what the receive path stored, where it stored it.
    [Test]
    public async Task AQueuedInboxItemsFilesAreWhereThePeerStreamedThem()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenarioAsync(sender, recipient, targetDrive);
        try
        {
            // Encrypted, so the recipient cannot write it straight to the drive and queues it in its Inbox
            var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
            payload.Iv = ByteArrayUtil.GetRndByteArray(16);
            var metadata = new UploadFileMetadata
            {
                AllowDistribution = true,
                AppData = new UploadAppFileMetaData { Content = "queued for sam" },
                AccessControlList = AccessControlList.Connected
            };
            var (response, _, _, _) = await sender.DriveRedux.UploadNewEncryptedFile(targetDrive, KeyHeader.NewRandom16(), metadata,
                ManifestFor(payload), [payload], recipients: [recipient.OdinId]);
            Assert.That(response.IsSuccessStatusCode, Is.True, response.StatusCode.ToString());
            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive);

            // Sam does not process his Inbox, as an owner who has not opened the app since
            await using var scope = TenantScope(TestIdentities.Samwise);
            var objects = (await PayloadMoveQueues.QueuedObjectsAsync(scope.Resolve<IdentityDatabase>()))
                .Where(o => o.DriveId == targetDrive.Alias).ToList();
            Assert.That(objects.Select(o => o.FileId).Distinct().Count(), Is.EqualTo(1), "the one queued item");
            Assert.That(objects, Has.Count.EqualTo(1 + payload.Thumbnails.Count), "the payload and each thumbnail");
            await AssertStoredAsync(scope, TestIdentities.Samwise, objects);
        }
        finally
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }
    }

    [Test]
    public async Task AnUnsentOutboxItemsFileIsAmongTheQueuedFiles()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenarioAsync(sender, recipient, targetDrive);
        try
        {
            // A paused recipient answers 503, so the item stays in Frodo's Outbox
            await SetStatusAsync(recipient.OdinId.DomainName, Status.Paused);
            var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
            var metadata = new UploadFileMetadata
            {
                AllowDistribution = true,
                AppData = new UploadAppFileMetaData { Content = "waiting to be sent" },
                AccessControlList = AccessControlList.Connected
            };
            var response = await sender.DriveRedux.UploadNewFile(targetDrive, metadata, ManifestFor(payload), [payload],
                new TransitOptions { Recipients = [recipient.OdinId] });
            Assert.That(response.IsSuccessStatusCode, Is.True, response.StatusCode.ToString());

            await using var scope = TenantScope(TestIdentities.Frodo);
            var queued = await PayloadMoveQueues.QueuedObjectsAsync(scope.Resolve<IdentityDatabase>());
            var objects = queued.Where(o => o.FileId == response.Content.File.FileId).ToList();
            Assert.That(objects, Has.Count.EqualTo(1 + payload.Thumbnails.Count), $"queued: {string.Join(", ", queued)}");
            await AssertStoredAsync(scope, TestIdentities.Frodo, objects);
        }
        finally
        {
            await SetStatusAsync(recipient.OdinId.DomainName, Status.Active);
            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }
    }

    [Test]
    public async Task ResumeWaitsForTheQueuedItemsPayloads()
    {
        var domain = TestIdentities.Frodo.OdinId.DomainName;
        var identityId = IdOf(TestIdentities.Frodo);
        var jobManager = _scaffold.Services.GetRequiredService<IJobManager>();

        // A transfer whose source cannot be reached, so its queued phase never finishes
        await PayloadMoveJob.ScheduleAsync(jobManager, identityId, "https://127.0.0.1:1", "token", 7);
        try
        {
            await SetStatusAsync(domain, Status.Paused);

            var refused = await SetStatusViaAdminAsync(domain, Status.Active, null);
            var body = await refused.Content.ReadAsStringAsync();
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), body);
            Assert.That(body, Does.Contain("still arriving"));

            // Out of quota runs the identity too: its Outbox would send
            var outOfQuota = await SetStatusViaAdminAsync(domain, Status.OutOfQuota, null);
            Assert.That(outOfQuota.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await outOfQuota.Content.ReadAsStringAsync());
            Assert.That(_scaffold.Services.GetRequiredService<IIdentityRegistry>().GetStatus(identityId), Is.EqualTo(Status.Paused));
        }
        finally
        {
            await jobManager.DeleteJobByHashAsync(PayloadMoveJob.JobHashFor(identityId));
        }

        await SetStatusAsync(domain, Status.Active);
    }

    //

    private ILifetimeScope TenantScope(TestIdentity identity) => _scaffold.Services.GetRequiredService<IMultiTenantContainer>()
        .GetTenantScope(identity.OdinId.DomainName).BeginLifetimeScope("PayloadMoveTests");

    private async Task AssertStoredAsync(ILifetimeScope scope, TestIdentity identity, List<PayloadObject> objects)
    {
        var store = scope.Resolve<LongTermPayloadStore>();
        var paths = new TenantPathManager(_scaffold.Services.GetRequiredService<OdinConfiguration>(), IdOf(identity));
        foreach (var o in objects)
        {
            Assert.That(await store.ExistsAsync(o.PathIn(paths)), Is.True, $"{o} at {o.PathIn(paths)}");
        }
    }

    private async Task<(DriveMainIndexRecord file, PayloadDescriptor payload, TargetDrive targetDrive)> UploadWithDriveAsync()
    {
        var owner = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var targetDrive = TargetDrive.NewTargetDrive();
        Assert.That((await owner.DriveManager.CreateDrive(targetDrive, "payload move", "", false)).IsSuccessStatusCode, Is.True);

        var definition = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var metadata = new UploadFileMetadata
        {
            IsEncrypted = false,
            AppData = new UploadAppFileMetaData { Content = "payload move" },
            AccessControlList = AccessControlList.OwnerOnly
        };
        var response = await owner.DriveRedux.UploadNewFile(targetDrive, metadata, ManifestFor(definition), [definition]);
        Assert.That(response.IsSuccessStatusCode, Is.True, response.StatusCode.ToString());
        var fileId = response.Content.File.FileId;

        // What the target will see: the file's row, and the payload descriptors inside it
        await using var scope = TenantScope(TestIdentities.Frodo);
        var (rows, _) = await scope.Resolve<IdentityDatabase>().DriveMainIndex.PagingByRowIdAsync(1000, null);
        var row = rows.Single(r => r.fileId == fileId);
        var fileMetadata = OdinSystemSerializer.Deserialize<FileMetadata>(row.hdrFileMetaData)!;
        return (row, fileMetadata.Payloads.Single(), targetDrive);
    }

    private PayloadMoveSource Source() => _scaffold.Services.GetRequiredService<PayloadMoveSource>();

    private Guid IdOf(TestIdentity identity) =>
        _scaffold.Services.GetRequiredService<IIdentityRegistry>().ResolveId(identity.OdinId.DomainName)!.Value;

    private Task<string> MintAsync() => Source().MintHandoffAsync(IdOf(TestIdentities.Frodo));

    private async Task<string> RedeemAsync(string token)
    {
        var response = await RedeemRawAsync(token);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return OdinSystemSerializer.Deserialize<PayloadMoveRedeemResponse>(await response.Content.ReadAsStringAsync())!.Credential;
    }

    private Task<HttpResponseMessage> RedeemRawAsync(string token)
    {
        var body = new StringContent(OdinSystemSerializer.Serialize(new PayloadMoveRedeemRequest { HandoffToken = token }),
            Encoding.UTF8, "application/json");
        return SendAsync(HttpMethod.Post, $"{Root()}/redeem", null, body);
    }

    private string Root() => PayloadMoveProtocol.IdentityPath(IdOf(TestIdentities.Frodo));

    private static string PayloadPath(DriveMainIndexRecord file, PayloadDescriptor payload) =>
        new PayloadObject(file.driveId, file.fileId, payload.Key, payload.Uid, 0).SourcePath(file.identityId);

    private static string ThumbnailPath(DriveMainIndexRecord file, PayloadDescriptor payload, ThumbnailDescriptor thumbnail) =>
        new PayloadObject(file.driveId, file.fileId, payload.Key, payload.Uid, 0, thumbnail.PixelWidth, thumbnail.PixelHeight)
            .SourcePath(file.identityId);

    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string credential,
        HttpContent content = null, string host = ProvisioningHost)
    {
        var authority = $"{host}:{WebScaffold.HttpsPort}";
        var client = WebScaffold.HttpClientFactory.CreateClient(authority);
        var request = new HttpRequestMessage(method, $"https://{authority}{path}") { Content = content };
        if (credential != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }

        return await client.SendAsync(request);
    }
}
