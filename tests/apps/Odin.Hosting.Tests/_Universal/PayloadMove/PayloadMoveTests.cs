using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
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
        var (file, payload) = await UploadAsync();
        await SetStatusAsync(TestIdentities.Frodo.OdinId.DomainName, Status.Paused);
        var credential = await RedeemAsync(await MintAsync());

        var get = await SendAsync(HttpMethod.Get, PayloadPath(file, payload), credential);
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.OK), await get.Content.ReadAsStringAsync());
        Assert.That(await get.Content.ReadAsByteArrayAsync(), Is.EqualTo(SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1().Content));

        var head = await SendAsync(HttpMethod.Head, PayloadPath(file, payload), credential);
        Assert.That(head.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(head.Content.Headers.ContentLength, Is.EqualTo(payload.BytesWritten));

        var thumbnail = payload.Thumbnails.Single();
        var thumb = await SendAsync(HttpMethod.Get, ThumbnailPath(file, payload, thumbnail), credential);
        Assert.That(thumb.StatusCode, Is.EqualTo(HttpStatusCode.OK), await thumb.Content.ReadAsStringAsync());
        Assert.That(await thumb.Content.ReadAsByteArrayAsync(), Is.EqualTo(TestMedia.ThumbnailBytes200));
    }

    [Test]
    public async Task RefusesEverythingElseWithA404()
    {
        var (file, payload) = await UploadAsync();
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
        System.IO.File.Delete(new TenantPathManager(config, identityId)
            .GetPayloadDirectoryAndFileName(file.driveId, file.fileId, payload.Key, payload.Uid));
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

            var retry = await SendAdminAsync(HttpMethod.Post, $"tenants/{domain}/payload-move/retry");
            Assert.That(retry.StatusCode, Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.Conflict), "conflict only if the runner holds it right now");
        }
        finally
        {
            await jobManager.DeleteJobByHashAsync(PayloadMoveJob.JobHashFor(identityId));
        }

        var noJob = await SendAdminAsync(HttpMethod.Post, $"tenants/{domain}/payload-move/retry");
        Assert.That(noJob.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    //

    private async Task<(DriveMainIndexRecord file, PayloadDescriptor payload)> UploadAsync()
    {
        var (file, payload, _) = await UploadWithDriveAsync();
        return (file, payload);
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
        var container = _scaffold.Services.GetRequiredService<IMultiTenantContainer>();
        await using var scope = container.GetTenantScope(TestIdentities.Frodo.OdinId.DomainName).BeginLifetimeScope("PayloadMoveTests");
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

    private string Root() => $"/api/payload-move/v1/{IdOf(TestIdentities.Frodo)}";

    private string PayloadPath(DriveMainIndexRecord file, PayloadDescriptor payload) =>
        $"/api/payload-move/v1/{file.identityId}/payload/{file.driveId}/{file.fileId}/{payload.Key}/{payload.Uid.uniqueTime}";

    private string ThumbnailPath(DriveMainIndexRecord file, PayloadDescriptor payload, ThumbnailDescriptor thumbnail) =>
        $"/api/payload-move/v1/{file.identityId}/thumb/{file.driveId}/{file.fileId}/{payload.Key}/{payload.Uid.uniqueTime}/" +
        $"{thumbnail.PixelWidth}x{thumbnail.PixelHeight}";

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
