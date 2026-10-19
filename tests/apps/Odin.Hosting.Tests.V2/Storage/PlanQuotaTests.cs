#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Admin.Tenants;
using Odin.Services.Base;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Registry;
using Refit;

namespace Odin.Hosting.Tests.V2.Storage;

public interface IStorageUsageClient
{
    [Get("/api/v2/storage/usage")]
    Task<ApiResponse<StorageUsage>> GetUsage();
}

/// <summary>
/// Plan quotas (docs/plan-quota.md) with the free plan's quota at 1 byte, so an identity is over it as soon as
/// it stores anything: headers still upload, payloads are refused with 507, and the usage endpoint says so to
/// the owner and apps.
/// </summary>
[TestFixture]
public class PlanQuotaTests : V2Fixture
{
    protected override IReadOnlyDictionary<string, string?> ConfigOverrides => new Dictionary<string, string?>
    {
        ["Registry:Plans:0:Id"] = "free",
        ["Registry:Plans:0:QuotaBytes"] = "1",
    };

    [Test]
    public async Task OverQuotaAHeaderStillUploads()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await CreateDriveAsync(owner);
        await StoreSomethingAsync(owner, drive);

        // Over the quota now, and a second header still goes in
        await StoreSomethingAsync(owner, drive);
    }

    [Test]
    public async Task OverQuotaAPayloadIsRefused()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await CreateDriveAsync(owner);
        await StoreSomethingAsync(owner, drive);

        var response = await UploadPayloadAsync(owner, drive);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InsufficientStorage), response.Error?.Content);
    }

    [Test]
    public async Task TheOwnerAndAnAppSeeTheUsage()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        await StoreSomethingAsync(owner, await CreateDriveAsync(owner));
        var app = await AppSession.SetupAsync(owner, new PermissionSetGrantRequest
        {
            Drives = [],
            PermissionSet = new PermissionSet(new List<int>())
        });

        var ownerUsage = (await owner.RefitFor<IStorageUsageClient>().GetUsage()).Content!;
        var appResponse = await app.RefitFor<IStorageUsageClient>().GetUsage();

        Assert.That(appResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(ownerUsage.QuotaBytes, Is.EqualTo(1));
        Assert.That(ownerUsage.UsedBytes, Is.GreaterThan(0));
        Assert.That(ownerUsage.IsOverQuota, Is.True);
        Assert.That(appResponse.Content, Is.EqualTo(ownerUsage));
    }

    internal static Task<ApiResponse<UploadResult>> UploadPayloadAsync(OwnerSession owner, TargetDrive drive)
    {
        var payloads = new List<TestPayloadDefinition> { SamplePayloadDefinitions.GetPayloadDefinition1() };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };
        return owner.V1.Drive.UploadNewFile(drive, SampleMetadataData.CreateWithContent(200, "x"), manifest, payloads);
    }

    // A header is enough to put the identity over a 1-byte quota
    internal static async Task StoreSomethingAsync(OwnerSession owner, TargetDrive drive)
    {
        var response = await owner.V1.Drive.UploadNewMetadata(drive, SampleMetadataData.Create(fileType: 100));
        Assert.That(response.IsSuccessStatusCode, Is.True, $"header upload: {response.StatusCode}");
    }

    internal static async Task<TargetDrive> CreateDriveAsync(OwnerSession owner)
    {
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "quota", allowAnonymousReads: false);
        return drive;
    }
}

/// <summary>
/// Without Registry:Plans there are no quotas: usage is reported, with no limit, and payloads upload.
/// </summary>
[TestFixture]
public class PlanQuotaOffTests : V2Fixture
{
    [Test]
    public async Task WithoutPlansThereIsNoQuota()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var usage = (await owner.RefitFor<IStorageUsageClient>().GetUsage()).Content!;

        Assert.That(usage.QuotaBytes, Is.Null);
        Assert.That(usage.IsOverQuota, Is.False);
    }
}

/// <summary>
/// An admin moves an identity to another plan, and its quota follows: over a 1-byte plan, a payload is refused;
/// on the unlimited plan, it goes in.
/// </summary>
[TestFixture]
public class SetTenantPlanTests : V2Fixture
{
    private const string AdminDomain = "admin.dotyou.cloud";
    private const int AdminPort = 4444;
    private const string AdminApiKey = "your-secret-api-key-here";
    private const string AdminApiKeyHeaderName = "Odin-Admin-Api-Key";
    private const string DevPlan = "dev-domain";

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides => new Dictionary<string, string?>
    {
        ["Admin:ApiEnabled"] = "true",
        ["Admin:ApiKey"] = AdminApiKey,
        ["Admin:ApiKeyHttpHeaderName"] = AdminApiKeyHeaderName,
        ["Admin:ApiPort"] = AdminPort.ToString(),
        ["Admin:Domain"] = AdminDomain,
        ["Registry:Plans:0:Id"] = "free",
        ["Registry:Plans:0:QuotaBytes"] = "1",
        ["Registry:Plans:1:Id"] = "unlimited",
        // The test identities' own plan (DevEnvironmentSetup), as tight as free so they start over quota
        ["Registry:Plans:2:Id"] = DevPlan,
        ["Registry:Plans:2:QuotaBytes"] = "1",
    };

    /// <summary>
    /// The plan lives in the identity registry, which the per-test reset does not restore
    /// </summary>
    [TearDown]
    public async Task RestorePlan()
    {
        await SetPlanAsync(Identities.Frodo.ToString(), DevPlan);
    }

    [Test]
    public async Task ANewPlanBringsItsQuota()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await PlanQuotaTests.CreateDriveAsync(owner);
        await PlanQuotaTests.StoreSomethingAsync(owner, drive);
        Assert.That((await PlanQuotaTests.UploadPayloadAsync(owner, drive)).StatusCode,
            Is.EqualTo(HttpStatusCode.InsufficientStorage));

        var response = await SetPlanAsync(Identities.Frodo.ToString(), " Unlimited ");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var change = OdinSystemSerializer.Deserialize<SetTenantPlanResponse>(await response.Content.ReadAsStringAsync());
        Assert.That(change!.PreviousPlanId, Is.EqualTo(DevPlan));

        var upload = await PlanQuotaTests.UploadPayloadAsync(owner, drive);
        Assert.That(upload.IsSuccessStatusCode, Is.True, upload.Error?.Content);

        var usage = (await owner.RefitFor<IStorageUsageClient>().GetUsage()).Content!;
        Assert.That(usage.PlanId, Is.EqualTo("Unlimited"));
        Assert.That(usage.QuotaBytes, Is.Null);
        Assert.That(usage.IsOverQuota, Is.False);
    }

    [Test]
    public async Task APlanNotInTheConfigIsRefused()
    {
        var response = await SetPlanAsync(Identities.Frodo.ToString(), "no-such-plan");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AnUnknownTenantIsNotFound()
    {
        var response = await SetPlanAsync("no-such-tenant.dotyou.cloud", "unlimited");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // TestServer leaves LocalPort at 0, and the admin API checks it; see AdminControllerTest
    private async Task<HttpResponseMessage> SetPlanAsync(string domain, string planId)
    {
        using var client = new HttpClient(Host.Server.CreateHandler(context => context.Connection.LocalPort = AdminPort))
        {
            BaseAddress = new Uri($"https://{AdminDomain}:{AdminPort}/")
        };
        var request = new HttpRequestMessage(HttpMethod.Patch, $"api/admin/v1/tenants/{domain}/plan")
        {
            Headers = { { AdminApiKeyHeaderName, AdminApiKey } },
            Content = new StringContent(OdinSystemSerializer.Serialize(new SetTenantPlanRequest { PlanId = planId }),
                Encoding.UTF8, "application/json")
        };
        return await client.SendAsync(request);
    }
}
