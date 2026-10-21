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
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Admin.Tenants;
using Odin.Services.Base;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Registry;
using Refit;

namespace Odin.Hosting.Tests.V2.Storage;

public interface IPlanClient
{
    [Get("/api/v2/plan")]
    Task<ApiResponse<IdentityPlan>> GetPlan();
}

/// <summary>
/// Plan quotas (docs/hosting-plans.md) with the free plan's quota at 1 byte, so an identity is over it as soon as
/// it stores anything: headers still upload, payloads are refused with 507, and the plan endpoint says so to
/// the owner and apps.
/// </summary>
[TestFixture]
public class PlanQuotaTests : V2Fixture
{
    protected override IReadOnlyDictionary<string, string?> ConfigOverrides => new Dictionary<string, string?>
    {
        ["Registry:Plans:0:Id"] = "free",
        ["Registry:Plans:0:Default"] = "true",
        ["Registry:Plans:0:QuotaBytes"] = "1",
        ["Registry:UpgradeUrl"] = "https://billing.example.com/upgrade",
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
    public async Task TheOwnerAndAnAppSeeThePlanAndItsUsage()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        await StoreSomethingAsync(owner, await CreateDriveAsync(owner));
        var app = await AppSession.SetupAsync(owner, new PermissionSetGrantRequest
        {
            Drives = [],
            PermissionSet = new PermissionSet(new List<int>())
        });

        var ownerPlan = (await owner.RefitFor<IPlanClient>().GetPlan()).Content!;
        var appResponse = await app.RefitFor<IPlanClient>().GetPlan();

        Assert.That(appResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        // The test identities' own plan is not in this config, so they get the default
        Assert.That(ownerPlan.Id, Is.EqualTo("free"));
        Assert.That(ownerPlan.IsDefault, Is.True);
        Assert.That(ownerPlan.PublicWebPresence, Is.True, "the plan does not say, and the identity's own is on");
        Assert.That(ownerPlan.Storage.QuotaBytes, Is.EqualTo(1));
        Assert.That(ownerPlan.Storage.UsedBytes, Is.GreaterThan(0));
        Assert.That(ownerPlan.Storage.IsOverQuota, Is.True);
        Assert.That(ownerPlan.UpgradeUrl, Does.StartWith($"https://billing.example.com/upgrade?id={Identities.Frodo}&host="));
        Assert.That(appResponse.Content, Is.EqualTo(ownerPlan));
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
/// Without Registry:Plans there are no plans and no quotas: usage is reported, with no limit, and payloads upload.
/// </summary>
[TestFixture]
public class PlanQuotaOffTests : V2Fixture
{
    [Test]
    public async Task WithoutPlansThereIsNoQuota()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var plan = (await owner.RefitFor<IPlanClient>().GetPlan()).Content!;

        Assert.That(plan.Id, Is.Null);
        Assert.That(plan.IsDefault, Is.False);
        Assert.That(plan.Storage.QuotaBytes, Is.Null);
        Assert.That(plan.Storage.IsOverQuota, Is.False);
        Assert.That(plan.UpgradeUrl, Is.Null, "no upgrade page configured");
    }
}

/// <summary>
/// An admin moves an identity to another plan, and what the plan gives follows: over a 1-byte plan, a payload is
/// refused; on the unlimited plan, it goes in. On a plan that says no public web presence, the public page goes.
/// </summary>
[TestFixture]
public class SetTenantPlanTests : V2Fixture
{
    private const string DevPlan = "dev-domain";

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides => new Dictionary<string, string?>(TestAdminApi.Config())
    {
        ["Registry:Plans:0:Id"] = "free",
        ["Registry:Plans:0:Default"] = "true",
        ["Registry:Plans:0:QuotaBytes"] = "1",
        ["Registry:Plans:1:Id"] = "unlimited",
        // The test identities' own plan (DevEnvironmentSetup), as tight as free so they start over quota
        ["Registry:Plans:2:Id"] = DevPlan,
        ["Registry:Plans:2:QuotaBytes"] = "1",
        ["Registry:Plans:3:Id"] = "private",
        ["Registry:Plans:3:PublicWebPresence"] = "false",
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

        var plan = (await owner.RefitFor<IPlanClient>().GetPlan()).Content!;
        Assert.That(plan.Id, Is.EqualTo("unlimited"), "the configured id, not the admin's spelling");
        Assert.That(plan.IsDefault, Is.False);
        Assert.That(plan.Storage.QuotaBytes, Is.Null);
        Assert.That(plan.Storage.IsOverQuota, Is.False);
    }

    [Test]
    public async Task ANewPlanBringsItsPublicWebPresence()
    {
        using var anonymous = Host.CreateAnonymousClient(Identities.Frodo);
        async Task<string> Home() => await (await anonymous.GetAsync("/ssr/home")).Content.ReadAsStringAsync();
        const string placeholder = "This is a Homebase ID.";

        Assert.That(await Home(), Does.Not.Contain(placeholder));

        Assert.That((await SetPlanAsync(Identities.Frodo.ToString(), "private")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await Home(), Does.Contain(placeholder), "the plan's no wins over the identity's yes");
        var owner = await LoginAsOwner(Identities.Frodo);
        Assert.That((await owner.RefitFor<IPlanClient>().GetPlan()).Content!.PublicWebPresence, Is.False);

        // A plan that does not say leaves it to the identity again, which never changed
        Assert.That((await SetPlanAsync(Identities.Frodo.ToString(), DevPlan)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await Home(), Does.Not.Contain(placeholder));
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

    private Task<HttpResponseMessage> SetPlanAsync(string domain, string planId) => TestAdminApi.SetPlanAsync(Host, domain, planId);
}
