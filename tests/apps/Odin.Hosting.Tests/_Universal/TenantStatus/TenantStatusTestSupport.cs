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
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Admin.Tenants;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;
using Status = Odin.Services.Registry.TenantStatus;

namespace Odin.Hosting.Tests._Universal.TenantStatus;

/// <summary>
/// Shared by the tenant-status test classes: the admin API, reading and draining an identity's
/// outbox, and the two-identity peer scenario.
/// </summary>
internal static class TenantStatusTestSupport
{
    public const string AdminApiKey = "your-secret-api-key-here";
    public const string AdminHost = "admin.dotyou.cloud:4444";

    public static Dictionary<string, string> AdminEnv() => new()
    {
        { "Admin__ApiEnabled", "true" },
        { "Admin__ApiKey", AdminApiKey },
        { "Admin__ApiKeyHttpHeaderName", "Odin-Admin-Api-Key" },
        { "Admin__ApiPort", "4444" },
        { "Admin__Domain", "admin.dotyou.cloud" },
    };

    public static async Task<HttpResponseMessage> SendAdminAsync(HttpMethod method, string path, HttpContent content = null)
    {
        var client = WebScaffold.HttpClientFactory.CreateClient(AdminHost);
        var request = new HttpRequestMessage(method, $"https://{AdminHost}/api/admin/v1/{path}")
        {
            Headers = { { "Odin-Admin-Api-Key", AdminApiKey } },
            Content = content
        };
        return await client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> PatchStatusRawAsync(string domain, string json)
    {
        return SendAdminAsync(HttpMethod.Patch, $"tenants/{domain}/status", new StringContent(json, Encoding.UTF8, "application/json"));
    }

    /// <summary>The raw admin response, for tests that expect a refusal.</summary>
    public static Task<HttpResponseMessage> SetStatusViaAdminAsync(string domain, Status status, DisabledReason? reason)
    {
        return PatchStatusRawAsync(domain,
            OdinSystemSerializer.Serialize(new SetTenantStatusRequest { Status = status, DisabledReason = reason }));
    }

    /// <summary>Sets the status and asserts the admin API accepted it.</summary>
    public static async Task SetStatusAsync(string domain, Status status)
    {
        var response = await SetStatusViaAdminAsync(domain, status, null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"setting {domain} to {status}");
    }

    /// <summary>
    /// Straight from the identity database. Filter by drive when the sending identity is shared by
    /// several tests: items another test left deferred (they wait minutes) are still in the outbox.
    /// </summary>
    public static async Task<List<OutboxRecord>> ReadOutboxAsync(WebScaffold scaffold, TestIdentity identity, TargetDrive drive = null)
    {
        var container = scaffold.Services.GetRequiredService<IMultiTenantContainer>();
        await using var scope = container.GetTenantScope(identity.OdinId.DomainName).BeginLifetimeScope("TenantStatusTests:read");
        var (records, _) = await scope.Resolve<IdentityDatabase>().Outbox.PagingByRowIdAsync(100, null);
        return drive == null ? records : records.Where(r => r.driveId == drive.Alias).ToList();
    }

    /// <summary>An item deferred by a "retry later" answer is scheduled minutes out; this makes it due now.</summary>
    public static async Task BringOutboxForwardAsync(WebScaffold scaffold, TestIdentity identity)
    {
        var container = scaffold.Services.GetRequiredService<IMultiTenantContainer>();
        await using var scope = container.GetTenantScope(identity.OdinId.DomainName).BeginLifetimeScope("TenantStatusTests:bringForward");
        await scope.Resolve<PeerOutbox>().BringForwardScheduledItemsAsync();
    }

    /// <summary>
    /// Brings scheduled items forward and processes them: the same drain hook the V2 test framework
    /// uses, since no test can wait out a deferral.
    /// </summary>
    public static async Task DrainOutboxAsync(WebScaffold scaffold, TestIdentity identity)
    {
        var container = scaffold.Services.GetRequiredService<IMultiTenantContainer>();
        await using var scope = container.GetTenantScope(identity.OdinId.DomainName).BeginLifetimeScope("TenantStatusTests:drain");
        await scope.Resolve<PeerOutbox>().BringForwardScheduledItemsAsync();
        await scope.Resolve<PeerOutboxProcessorBackgroundService>().DrainAsync();
    }

    public static Task WaitUntilAsync(Func<bool> condition, string what, TimeSpan? timeout = null)
    {
        return WaitUntilAsync(() => Task.FromResult(condition()), what, timeout);
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> condition, string what, TimeSpan? timeout = null)
    {
        var sw = Stopwatch.StartNew();
        while (!await condition())
        {
            if (sw.Elapsed > (timeout ?? TimeSpan.FromSeconds(30)))
            {
                Assert.Fail($"Timed out waiting for {what}");
            }
            await Task.Delay(50);
        }
    }

    public static UploadManifest ManifestFor(TestPayloadDefinition payload)
    {
        return new UploadManifest { PayloadDescriptors = new List<TestPayloadDefinition> { payload }.ToPayloadDescriptorList().ToList() };
    }

    /// <summary>Same drive on both sides, and the sender connected into a circle that may write to it.</summary>
    public static async Task PrepareScenarioAsync(OwnerApiClientRedux sender, OwnerApiClientRedux recipient, TargetDrive targetDrive)
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
