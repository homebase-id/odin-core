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
using Odin.Core.Json;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.PubSub;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.AppNotifications.WebSocket;
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
using Refit;
using Status = Odin.Services.Registry.TenantStatus;
using static Odin.Hosting.Tests._Universal.TenantStatus.TenantStatusTestSupport;

namespace Odin.Hosting.Tests._Universal.TenantStatus;

/// <summary>
/// End-to-end behaviour of the tenant status ladder: admin API, HTTP responses, background services
/// stopping and starting, and other nodes applying a change they only read from the database.
/// </summary>
public class TenantStatusTests
{
    private WebScaffold _scaffold = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        var env = AdminEnv();
        _scaffold.RunBeforeAnyTests(envOverrides: env,
            testIdentities: [TestIdentities.Frodo, TestIdentities.Samwise, TestIdentities.Pippin]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [TearDown]
    public async Task ResetStatuses()
    {
        // Straight to the database: some tests leave an identity in a state the API refuses to leave (moved)
        foreach (var identity in new[] { TestIdentities.Frodo, TestIdentities.Samwise, TestIdentities.Pippin })
        {
            if (Registry.GetAsync(identity.OdinId.DomainName).Result.Status != Status.Active)
            {
                await WriteRowAsOtherNodeAsync(identity, disabled: false, json: null);
                await WaitUntilAsync(() => Registry.AreBackgroundServicesRunning(IdOf(identity)), "background services restart");
            }
        }
    }

    //
    // Admin API
    //

    [Test]
    public async Task AdminApiSetsAndReportsEveryStatus()
    {
        var domain = TestIdentities.Frodo.OdinId.DomainName;

        var steps = new (Status Status, DisabledReason? Reason, DisabledReason? ExpectedReason)[]
        {
            (Status.OutOfQuota, null, null),
            (Status.Paused, null, null),
            (Status.Disabled, null, DisabledReason.Admin),
            (Status.Disabled, DisabledReason.PendingDeletion, DisabledReason.PendingDeletion),
            (Status.Active, null, null),
        };

        var previous = new TenantStatusState(Status.Active, null, null);
        foreach (var step in steps)
        {
            var before = UnixTimeUtcNow();
            var response = await SetStatusViaAdminAsync(domain, step.Status, step.Reason);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"setting {step.Status}");

            var returned = OdinSystemSerializer.Deserialize<TenantStatusState>(await response.Content.ReadAsStringAsync())!;
            Assert.That(returned.Status, Is.EqualTo(previous.Status), "response carries the previous status");
            Assert.That(returned.DisabledReason, Is.EqualTo(previous.DisabledReason));

            var tenant = await GetTenantViaAdminAsync(domain);
            Assert.That(tenant.Status, Is.EqualTo(step.Status));
            Assert.That(tenant.DisabledReason, Is.EqualTo(step.ExpectedReason));
            Assert.That(tenant.Enabled, Is.EqualTo(step.Status != Status.Disabled));
            Assert.That(tenant.StatusChangedAt, Is.Not.Null);
            Assert.That(tenant.StatusChangedAt!.Value.milliseconds, Is.GreaterThanOrEqualTo(before));

            previous = new TenantStatusState(tenant.Status, tenant.DisabledReason, null);
        }
    }

    [Test]
    public async Task AdminApiSettingTheSameStatusAgainRecordsANewChangeTime()
    {
        // A deliberate re-pause must be distinguishable from the pause already in place
        var domain = TestIdentities.Frodo.OdinId.DomainName;
        await SetStatusViaAdminAsync(domain, Status.Paused, null);
        var first = (await GetTenantViaAdminAsync(domain)).StatusChangedAt;

        await Task.Delay(20);
        var response = await SetStatusViaAdminAsync(domain, Status.Paused, null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var returned = OdinSystemSerializer.Deserialize<TenantStatusState>(await response.Content.ReadAsStringAsync())!;
        Assert.That(returned.Status, Is.EqualTo(Status.Paused), "reports it was already paused");

        var second = (await GetTenantViaAdminAsync(domain)).StatusChangedAt;
        Assert.That(second!.Value.milliseconds, Is.GreaterThan(first!.Value.milliseconds));
        Assert.That(Registry.AreBackgroundServicesRunning(IdOf(TestIdentities.Frodo)), Is.False);
    }

    [TestCase("{}")]
    [TestCase("{\"stauts\":\"active\"}")]
    [TestCase("{\"status\":null}")]
    public async Task AdminApiRefusesAMissingStatus(string body)
    {
        // A missing status must not bind to Active and re-enable a disabled tenant
        var domain = TestIdentities.Frodo.OdinId.DomainName;
        await SetStatusViaAdminAsync(domain, Status.Disabled, DisabledReason.Admin);

        var response = await PatchStatusRawAsync(domain, body);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await GetTenantViaAdminAsync(domain)).Status, Is.EqualTo(Status.Disabled));
    }

    [Test]
    public async Task AdminApiReturnsNotFoundForUnknownTenant()
    {
        var response = await SetStatusViaAdminAsync("nobody.dotyou.cloud", Status.Paused, null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task AdminApiRefusesAReasonWithoutDisabled()
    {
        var domain = TestIdentities.Frodo.OdinId.DomainName;
        var response = await SetStatusViaAdminAsync(domain, Status.Paused, DisabledReason.Moved);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await GetTenantViaAdminAsync(domain)).Status, Is.EqualTo(Status.Active));
    }

    [Test]
    public async Task AdminApiRefusesAnUnknownStatus()
    {
        var domain = TestIdentities.Frodo.OdinId.DomainName;
        var response = await PatchStatusRawAsync(domain, "{\"status\":\"frozen\"}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await GetTenantViaAdminAsync(domain)).Status, Is.EqualTo(Status.Active));
    }

    [Test]
    public async Task MovedIdentityCannotBeReEnabledButCanBeDeleted()
    {
        var domain = TestIdentities.Pippin.OdinId.DomainName;
        Assert.That((await SetStatusViaAdminAsync(domain, Status.Disabled, DisabledReason.Moved)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        foreach (var (status, reason) in new (Status, DisabledReason?)[]
                 {
                     (Status.Active, null), (Status.OutOfQuota, null), (Status.Paused, null), (Status.Disabled, DisabledReason.Admin)
                 })
        {
            var refused = await SetStatusViaAdminAsync(domain, status, reason);
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"{status}/{reason}");
        }

        // The old enable endpoint is refused as well
        var enable = await SendAdminAsync(HttpMethod.Patch, $"tenants/{domain}/enable");
        Assert.That(enable.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var tenant = await GetTenantViaAdminAsync(domain);
        Assert.That(tenant.Status, Is.EqualTo(Status.Disabled));
        Assert.That(tenant.DisabledReason, Is.EqualTo(DisabledReason.Moved));

        // Deleting the leftover copy is allowed
        var pendingDeletion = await SetStatusViaAdminAsync(domain, Status.Disabled, DisabledReason.PendingDeletion);
        Assert.That(pendingDeletion.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task DisabledIdentityCanOnlyBeEnabled()
    {
        // Otherwise pause-then-resume would quietly re-enable it
        var domain = TestIdentities.Frodo.OdinId.DomainName;
        await SetStatusViaAdminAsync(domain, Status.Disabled, DisabledReason.Admin);

        Assert.That((await SetStatusViaAdminAsync(domain, Status.Paused, null)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await SetStatusViaAdminAsync(domain, Status.OutOfQuota, null)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await GetTenantViaAdminAsync(domain)).Status, Is.EqualTo(Status.Disabled));
        Assert.That(Registry.AreBackgroundServicesRunning(IdOf(TestIdentities.Frodo)), Is.False);

        Assert.That((await SetStatusViaAdminAsync(domain, Status.Active, null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(Registry.AreBackgroundServicesRunning(IdOf(TestIdentities.Frodo)), Is.True);
    }

    [Test]
    public async Task EnableAndDisableEndpointsKeepWorkingAsWrappers()
    {
        var domain = TestIdentities.Frodo.OdinId.DomainName;

        Assert.That((await SendAdminAsync(HttpMethod.Patch, $"tenants/{domain}/disable")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var disabled = await GetTenantViaAdminAsync(domain);
        Assert.That(disabled.Status, Is.EqualTo(Status.Disabled));
        Assert.That(disabled.DisabledReason, Is.EqualTo(DisabledReason.Admin));
        Assert.That(disabled.Enabled, Is.False);

        Assert.That((await SendAdminAsync(HttpMethod.Patch, $"tenants/{domain}/enable")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var enabled = await GetTenantViaAdminAsync(domain);
        Assert.That(enabled.Status, Is.EqualTo(Status.Active));
        Assert.That(enabled.DisabledReason, Is.Null);

        // Enable leaves a paused identity paused: it only undoes disable
        await SetStatusViaAdminAsync(domain, Status.Paused, null);
        Assert.That((await SendAdminAsync(HttpMethod.Patch, $"tenants/{domain}/enable")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await GetTenantViaAdminAsync(domain)).Status, Is.EqualTo(Status.Paused));

        // Disabling an identity already disabled for deletion keeps that reason
        await SetStatusViaAdminAsync(domain, Status.Disabled, DisabledReason.PendingDeletion);
        Assert.That((await SendAdminAsync(HttpMethod.Patch, $"tenants/{domain}/disable")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await GetTenantViaAdminAsync(domain)).DisabledReason, Is.EqualTo(DisabledReason.PendingDeletion));
    }

    //
    // HTTP responses
    //

    private static readonly string[] ProbePaths =
    [
        "/api/owner/v1/authentication/verifyToken", // owner
        "/api/apps/v1/auth/verifytoken", // app
        "/api/guest/v1/builtin/home/auth/is-authenticated", // guest
        "/api/v2/drives/status", // v2
        "/api/v1/perimeter/transit/host/file/upload", // peer
        "/", // web; only probed while blocked, since serving it needs the web app's dev server
    ];

    private static readonly string[] ServingProbePaths = ProbePaths.Where(p => p != "/").ToArray();

    [Test]
    public async Task PausedIdentityTellsEveryCallerToRetryLater()
    {
        var identity = TestIdentities.Samwise;
        await SetStatusViaAdminAsync(identity.OdinId.DomainName, Status.Paused, null);

        foreach (var path in ProbePaths)
        {
            var response = await ProbeAsync(identity, path);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable), path);
            Assert.That(response.Headers.RetryAfter?.Delta, Is.EqualTo(TimeSpan.FromSeconds(TenantStatusRules.PausedRetryAfterSeconds)), path);
            Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("is paused"), path);
        }

        // Certificate issuance keeps working: ACME is answered before tenant resolution
        var acme = await ProbeAsync(identity, "/.well-known/acme-challenge/ping");
        Assert.That(acme.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await SetStatusViaAdminAsync(identity.OdinId.DomainName, Status.Active, null);
        foreach (var path in ServingProbePaths)
        {
            var response = await ProbeAsync(identity, path);
            Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.ServiceUnavailable).And.Not.EqualTo(HttpStatusCode.Conflict), path);
        }
    }

    [Test]
    public async Task DisabledIdentityStillAnswersConflict()
    {
        var identity = TestIdentities.Samwise;
        await SetStatusViaAdminAsync(identity.OdinId.DomainName, Status.Disabled, null);

        foreach (var path in ProbePaths)
        {
            var response = await ProbeAsync(identity, path);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), path);
            Assert.That(response.Headers.RetryAfter, Is.Null, path);
        }
    }

    [Test]
    public async Task OutOfQuotaIdentityStillServesRequests()
    {
        // Enforcement comes later; for now the status must not block anything
        var identity = TestIdentities.Samwise;
        await SetStatusViaAdminAsync(identity.OdinId.DomainName, Status.OutOfQuota, null);

        var owner = _scaffold.CreateOwnerApiClientRedux(identity);
        var drives = await owner.DriveManager.GetDrives();
        Assert.That(drives.IsSuccessStatusCode, Is.True);
        Assert.That(Registry.AreBackgroundServicesRunning(IdOf(identity)), Is.True);
    }

    //
    // Background services
    //

    [TestCase(Status.Paused, null)]
    [TestCase(Status.Disabled, DisabledReason.Admin)]
    public async Task BackgroundServicesStopAndStartWithTheStatus(Status status, DisabledReason? reason)
    {
        var identity = TestIdentities.Samwise;
        var id = IdOf(identity);
        Assert.That(Registry.AreBackgroundServicesRunning(id), Is.True, "running before");

        await SetStatusViaAdminAsync(identity.OdinId.DomainName, status, reason);
        // The admin call returns once services are stopped on this node, so no waiting
        Assert.That(Registry.AreBackgroundServicesRunning(id), Is.False, "stopped");

        await SetStatusViaAdminAsync(identity.OdinId.DomainName, Status.Active, null);
        Assert.That(Registry.AreBackgroundServicesRunning(id), Is.True, "running again");

        await SetStatusViaAdminAsync(identity.OdinId.DomainName, Status.OutOfQuota, null);
        Assert.That(Registry.AreBackgroundServicesRunning(id), Is.True, "running when out of quota");

        await SetStatusViaAdminAsync(identity.OdinId.DomainName, status, reason);
        Assert.That(Registry.AreBackgroundServicesRunning(id), Is.False, "stopped from out of quota");

        await SetStatusViaAdminAsync(identity.OdinId.DomainName, Status.Active, null);
        Assert.That(Registry.AreBackgroundServicesRunning(id), Is.True, "running at the end");

        // The other identities were never touched
        Assert.That(Registry.AreBackgroundServicesRunning(IdOf(TestIdentities.Frodo)), Is.True);
    }

    [TestCase(Status.Paused, null)]
    [TestCase(Status.Disabled, DisabledReason.Admin)]
    public async Task OpenSocketsAreClosedWhenTheIdentityStops(Status status, DisabledReason? reason)
    {
        // A socket accepted before the status changed could otherwise keep issuing commands
        var identity = TestIdentities.Samwise;
        var owner = _scaffold.CreateOwnerApiClientRedux(identity);
        var listener = new TestOwnerWebSocketListener();
        await listener.ConnectAsync(identity.OdinId, owner.GetTokenContext(), new EstablishConnectionOptions { Drives = [] });
        Assert.That(listener.State, Is.EqualTo(System.Net.WebSockets.WebSocketState.Open));

        try
        {
            await SetStatusViaAdminAsync(identity.OdinId.DomainName, status, reason);
            await WaitUntilAsync(() => listener.State != System.Net.WebSockets.WebSocketState.Open, "socket to close");
        }
        finally
        {
            await listener.DisconnectAsync();
        }
    }

    [Test]
    public async Task TransferToPausedRecipientWaitsInSenderOutboxAndArrivesAfterResume()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenarioAsync(sender, recipient, targetDrive);

        try
        {
            await SetStatusViaAdminAsync(recipient.OdinId.DomainName, Status.Paused, null);

            var uploadResult = await UploadAsync(sender, targetDrive, recipient.OdinId);

            // The recipient answers 503, which the sender treats as retryable: the item stays
            await WaitUntilAsync(async () =>
            {
                var history = await sender.DriveRedux.GetTransferHistory(uploadResult.File);
                var item = history.Content?.GetHistoryItem(recipient.OdinId);
                return item?.LatestTransferStatus == LatestTransferStatus.RecipientIdentityReturnedServerError;
            }, "sender to record the 503");
            var outbox = await sender.DriveRedux.GetDriveStatus(targetDrive);
            Assert.That(outbox.Content.Outbox.TotalItems, Is.EqualTo(1));

            await SetStatusViaAdminAsync(recipient.OdinId.DomainName, Status.Active, null);

            // A paused recipient asks for ten minutes and the sender honours that, so a test has to
            // bring the item forward rather than wait it out
            await DrainOutboxAsync(_scaffold, sender.Identity);
            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive, TimeSpan.FromSeconds(90));
            await recipient.DriveRedux.ProcessInbox(targetDrive);
            var received = await recipient.DriveRedux.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
            Assert.That(received.Content.SearchResults.Count(), Is.EqualTo(1), "recipient has the file after resume");
        }
        finally
        {
            await SetStatusViaAdminAsync(recipient.OdinId.DomainName, Status.Active, null);
            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }
    }

    [Test]
    public async Task PausedSenderDoesNotSendFromItsOutbox()
    {
        var sender = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var recipient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenarioAsync(sender, recipient, targetDrive);

        try
        {
            // Queue an item in the sender's outbox by making the first attempt fail
            await SetStatusViaAdminAsync(recipient.OdinId.DomainName, Status.Paused, null);
            var uploadResult = await UploadAsync(sender, targetDrive, recipient.OdinId);
            await WaitUntilAsync(async () =>
            {
                var history = await sender.DriveRedux.GetTransferHistory(uploadResult.File);
                return history.Content?.GetHistoryItem(recipient.OdinId)?.LatestTransferStatus ==
                       LatestTransferStatus.RecipientIdentityReturnedServerError;
            }, "first attempt to fail");

            // Pause the sender, then let the recipient accept again
            await SetStatusViaAdminAsync(sender.OdinId.DomainName, Status.Paused, null);
            await SetStatusViaAdminAsync(recipient.OdinId.DomainName, Status.Active, null);

            // Make the queued item due right now, then wait a while: a running outbox would check it out
            // (bumping checkOutCount) or deliver it; a stopped one leaves the row exactly as it is.
            // (The recipient's 503 defers the item for ten minutes, which a test cannot wait out.)
            var queued = await ReadOutboxAsync(_scaffold, sender.Identity);
            Assert.That(queued, Has.Count.EqualTo(1));
            await BringOutboxForwardAsync(_scaffold, sender.Identity);
            await Task.Delay(TimeSpan.FromSeconds(5));

            var untouched = await ReadOutboxAsync(_scaffold, sender.Identity);
            Assert.That(untouched, Has.Count.EqualTo(1), "a paused sender must not deliver");
            Assert.That(untouched[0].checkOutCount, Is.EqualTo(queued[0].checkOutCount), "a paused sender must not even try");
            Assert.That(untouched[0].checkOutStamp, Is.Null);

            await recipient.DriveRedux.ProcessInbox(targetDrive);
            var notYet = await recipient.DriveRedux.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
            Assert.That(notYet.Content.SearchResults, Is.Empty);

            // Resume the sender: its outbox runs again and delivers
            await SetStatusViaAdminAsync(sender.OdinId.DomainName, Status.Active, null);
            await sender.DriveRedux.WaitForEmptyOutbox(targetDrive, TimeSpan.FromSeconds(90));
            await recipient.DriveRedux.ProcessInbox(targetDrive);
            var received = await recipient.DriveRedux.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
            Assert.That(received.Content.SearchResults.Count(), Is.EqualTo(1), "delivered after the sender resumed");
        }
        finally
        {
            await SetStatusViaAdminAsync(sender.OdinId.DomainName, Status.Active, null);
            await SetStatusViaAdminAsync(recipient.OdinId.DomainName, Status.Active, null);
            await _scaffold.OldOwnerApi.DisconnectIdentities(sender.OdinId, recipient.OdinId);
        }
    }

    //
    // Other nodes, legacy rows, persistence
    //

    [Test]
    public async Task StatusChangedByAnotherNodeIsAppliedHere()
    {
        var identity = TestIdentities.Samwise;
        var id = IdOf(identity);

        await WriteRowAsOtherNodeAsync(identity, disabled: false,
            json: "{\"status\":\"paused\",\"statusChangedAt\":1757000000000}");

        await WaitUntilAsync(() => Registry.GetAsync(identity.OdinId.DomainName).Result.Status == Status.Paused, "status applied");
        await WaitUntilAsync(() => !Registry.AreBackgroundServicesRunning(id), "background services stopped");
        Assert.That((await ProbeAsync(identity, ProbePaths[0])).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That((await GetTenantViaAdminAsync(identity.OdinId.DomainName)).StatusChangedAt?.milliseconds, Is.EqualTo(1757000000000));

        await WriteRowAsOtherNodeAsync(identity, disabled: false, json: "{\"status\":\"active\"}");

        await WaitUntilAsync(() => Registry.AreBackgroundServicesRunning(id), "background services restarted");
        Assert.That((await ProbeAsync(identity, ProbePaths[0])).StatusCode, Is.Not.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    [Test]
    public async Task LegacyDisabledRowWithoutJsonReadsAsDisabledByAdmin()
    {
        // What a node without the json column writes
        var identity = TestIdentities.Samwise;
        await WriteRowAsOtherNodeAsync(identity, disabled: true, json: null);

        await WaitUntilAsync(() => Registry.GetAsync(identity.OdinId.DomainName).Result.Status == Status.Disabled, "status applied");
        var registration = await Registry.GetAsync(identity.OdinId.DomainName);
        Assert.That(registration.DisabledReason, Is.EqualTo(DisabledReason.Admin));
        await WaitUntilAsync(() => !Registry.AreBackgroundServicesRunning(IdOf(identity)), "background services stopped");
        Assert.That((await ProbeAsync(identity, ProbePaths[0])).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task UnrelatedRegistryWritesKeepTheStatus()
    {
        // Every registry save rewrites the whole row; the status must survive them
        var identity = TestIdentities.Samwise;
        var domain = identity.OdinId.DomainName;
        await SetStatusViaAdminAsync(domain, Status.Disabled, DisabledReason.PendingDeletion);

        Assert.That((await SendAdminAsync(HttpMethod.Patch, $"tenants/{domain}/public-web-presence/disable")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await SendAdminAsync(HttpMethod.Patch, $"tenants/{domain}/public-web-presence/enable")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var row = await ReadRowAsync(identity);
        Assert.That(row.disabled, Is.True);
        Assert.That(row.json, Does.Contain("\"disabled\"").And.Contain("\"pendingDeletion\""));

        // A reload from the database (as another node would do) reads the status back from the json
        var reloadedJson = row.json.Replace(row.json.Split("\"statusChangedAt\":")[1].Split('}')[0].Split(',')[0], "1757000000123");
        await WriteRowAsOtherNodeAsync(identity, row.disabled, reloadedJson);
        await WaitUntilAsync(() => Registry.GetAsync(domain).Result.StatusChangedAt?.milliseconds == 1757000000123, "reload");
        var registration = await Registry.GetAsync(domain);
        Assert.That(registration.Status, Is.EqualTo(Status.Disabled));
        Assert.That(registration.DisabledReason, Is.EqualTo(DisabledReason.PendingDeletion));
    }

    //
    // Helpers
    //

    private FileSystemIdentityRegistry Registry =>
        (FileSystemIdentityRegistry)_scaffold.Services.GetRequiredService<IIdentityRegistry>();

    private Guid IdOf(TestIdentity identity) => Registry.ResolveId(identity.OdinId.DomainName)!.Value;

    private static long UnixTimeUtcNow() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1;

    private static async Task<TenantModel> GetTenantViaAdminAsync(string domain)
    {
        var response = await SendAdminAsync(HttpMethod.Get, $"tenants/{domain}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return OdinSystemSerializer.Deserialize<TenantModel>(await response.Content.ReadAsStringAsync())!;
    }

    private static async Task<HttpResponseMessage> ProbeAsync(TestIdentity identity, string path)
    {
        var host = $"{identity.OdinId.DomainName}:{WebScaffold.HttpsPort}";
        var client = WebScaffold.HttpClientFactory.CreateClient(host);
        return await client.GetAsync($"https://{host}{path}");
    }

    private async Task<Odin.Core.Storage.Database.System.Table.RegistrationsRecord> ReadRowAsync(TestIdentity identity)
    {
        var container = _scaffold.Services.GetRequiredService<IMultiTenantContainer>();
        await using var scope = container.BeginLifetimeScope("TenantStatusTests");
        var systemDatabase = scope.Resolve<SystemDatabase>();
        var id = IdOf(identity);
        return (await systemDatabase.Registrations.GetAllAsync()).Single(r => r.identityId == id);
    }

    // Changes the row and bumps the registry version the way another node would, then announces it
    // with a foreign node id, so this node reconciles from the database instead of its own write path
    private async Task WriteRowAsOtherNodeAsync(TestIdentity identity, bool disabled, string json)
    {
        var container = _scaffold.Services.GetRequiredService<IMultiTenantContainer>();
        long version;
        await using (var scope = container.BeginLifetimeScope("TenantStatusTests"))
        {
            var systemDatabase = scope.Resolve<SystemDatabase>();
            var id = IdOf(identity);
            await using var tx = await systemDatabase.BeginStackedTransactionAsync();
            var record = (await systemDatabase.Registrations.GetAllAsync()).Single(r => r.identityId == id);
            record.disabled = disabled;
            record.json = json;
            await systemDatabase.Registrations.UpdateAsync(record);
            (_, version) = await systemDatabase.Settings.BumpMonotonicAsync("registry-version");
            tx.Commit();
        }

        var pubSub = container.Resolve<ISystemPubSub>();
        await pubSub.PublishAsync(RegistryChangeMessage.Channel,
            JsonEnvelope.Create(new RegistryChangeMessage { Version = version, OriginNodeId = Guid.NewGuid() }));
    }

    private static async Task<UploadResult> UploadAsync(OwnerApiClientRedux sender, TargetDrive targetDrive, OdinId recipient)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new() { Content = "status ladder" },
            AccessControlList = AccessControlList.Connected
        };

        var (response, _) = await sender.DriveRedux.UploadNewEncryptedMetadata(
            fileMetadata,
            new StorageOptions { Drive = targetDrive },
            new TransitOptions { Recipients = [recipient] });

        ClassicAssert.IsTrue(response.IsSuccessStatusCode);
        Assert.That(response.Content.RecipientStatus[recipient], Is.EqualTo(TransferStatus.Enqueued));
        return response.Content;
    }
}
