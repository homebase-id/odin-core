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
using Odin.Services.Configuration;
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
/// Quotas (docs/hosting-plans.md) with the host's default quota at 1 byte, so an identity without one of its own
/// is over it as soon as it stores anything: headers still upload, payloads are refused with 507, and the plan
/// endpoint says so to the owner and apps, with the hosting provider to ask for more.
/// </summary>
[TestFixture]
public class PlanQuotaTests : V2Fixture
{
    protected override IReadOnlyDictionary<string, string?> ConfigOverrides => new Dictionary<string, string?>
    {
        ["HostingProvider:DefaultQuotaBytes"] = "1",
        ["HostingProvider:DisplayName"] = "Test Hosting",
        ["HostingProvider:UpgradeUrl"] = "https://billing.example.com/upgrade",
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
    public async Task TheOwnerAndAnAppSeeThePlanItsUsageAndTheHostingProvider()
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
        // The test identities' own plan label (DevEnvironmentSetup), with no quota of their own: the default
        Assert.That(ownerPlan.Id, Is.EqualTo("dev-domain"));
        Assert.That(ownerPlan.PublicWebPresence, Is.True);
        Assert.That(ownerPlan.Storage.QuotaBytes, Is.EqualTo(1));
        Assert.That(ownerPlan.Storage.UsedBytes, Is.GreaterThan(0));
        Assert.That(ownerPlan.Storage.IsOverQuota, Is.True);
        Assert.That(ownerPlan.HostingProvider!.DisplayName, Is.EqualTo("Test Hosting"));
        Assert.That(ownerPlan.HostingProvider.UpgradeUrl,
            Does.StartWith($"https://billing.example.com/upgrade?id={Identities.Frodo}&host="));
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
/// With no hosting provider configured: every identity has the default quota, 60 GB, and nobody to ask for more.
/// </summary>
[TestFixture]
public class PlanWithoutAHostingProviderTests : V2Fixture
{
    [Test]
    public async Task TheDefaultQuotaAndNoHostingProvider()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var plan = (await owner.RefitFor<IPlanClient>().GetPlan()).Content!;

        Assert.That(plan.Storage.QuotaBytes, Is.EqualTo(OdinConfiguration.HostingProviderSection.SixtyGigabytes));
        Assert.That(plan.Storage.IsOverQuota, Is.False);
        Assert.That(plan.HostingProvider, Is.Null);
    }
}

/// <summary>
/// An admin sets an identity's plan with what it gives: a bigger quota lets a payload in, a public web presence of
/// false takes the public page down, and values left out stay as they were.
/// </summary>
[TestFixture]
public class SetTenantPlanTests : V2Fixture
{
    private const string DevPlan = "dev-domain";

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides => new Dictionary<string, string?>(TestAdminApi.Config())
    {
        // So the test identities, with no quota of their own, start over it
        ["HostingProvider:DefaultQuotaBytes"] = "1",
    };

    /// <summary>
    /// The plan lives in the identity registry, which the per-test reset does not restore
    /// </summary>
    [TearDown]
    public async Task RestorePlan()
    {
        await SetPlanAsync(Identities.Frodo.ToString(), DevPlan, 1, true);
    }

    [Test]
    public async Task ABiggerQuotaLetsAPayloadIn()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await PlanQuotaTests.CreateDriveAsync(owner);
        await PlanQuotaTests.StoreSomethingAsync(owner, drive);
        Assert.That((await PlanQuotaTests.UploadPayloadAsync(owner, drive)).StatusCode,
            Is.EqualTo(HttpStatusCode.InsufficientStorage));

        var response = await SetPlanAsync(Identities.Frodo.ToString(), " veterans ", 1_099_511_627_776);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var change = OdinSystemSerializer.Deserialize<SetTenantPlanResponse>(await response.Content.ReadAsStringAsync());
        Assert.That(change!.PreviousPlanId, Is.EqualTo(DevPlan));

        var upload = await PlanQuotaTests.UploadPayloadAsync(owner, drive);
        Assert.That(upload.IsSuccessStatusCode, Is.True, upload.Error?.Content);

        var plan = (await owner.RefitFor<IPlanClient>().GetPlan()).Content!;
        Assert.That(plan.Id, Is.EqualTo("veterans"), "trimmed");
        Assert.That(plan.Storage.QuotaBytes, Is.EqualTo(1_099_511_627_776));
        Assert.That(plan.Storage.IsOverQuota, Is.False);
    }

    [Test]
    public async Task APlanWithoutAPublicWebPresenceTakesThePageDown()
    {
        using var anonymous = Host.CreateAnonymousClient(Identities.Frodo);
        async Task<string> Home() => await (await anonymous.GetAsync("/ssr/home")).Content.ReadAsStringAsync();
        const string placeholder = "This is a Homebase ID.";

        Assert.That(await Home(), Does.Not.Contain(placeholder));

        Assert.That((await SetPlanAsync(Identities.Frodo.ToString(), "private", publicWebPresence: false)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await Home(), Does.Contain(placeholder));
        var owner = await LoginAsOwner(Identities.Frodo);
        Assert.That((await owner.RefitFor<IPlanClient>().GetPlan()).Content!.PublicWebPresence, Is.False);

        Assert.That((await SetPlanAsync(Identities.Frodo.ToString(), DevPlan, publicWebPresence: true)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await Home(), Does.Not.Contain(placeholder));
    }

    [Test]
    public async Task ValuesLeftOutStayAsTheyWere()
    {
        Assert.That((await SetPlanAsync(Identities.Frodo.ToString(), "first", 5000, false)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await SetPlanAsync(Identities.Frodo.ToString(), "second")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var owner = await LoginAsOwner(Identities.Frodo);
        var plan = (await owner.RefitFor<IPlanClient>().GetPlan()).Content!;
        Assert.That(plan.Id, Is.EqualTo("second"));
        Assert.That(plan.Storage.QuotaBytes, Is.EqualTo(5000));
        Assert.That(plan.PublicWebPresence, Is.False);
    }

    [TestCase(0L)]
    [TestCase(-5L)]
    public async Task AQuotaOfNothingIsRefused(long quotaBytes)
    {
        var response = await SetPlanAsync(Identities.Frodo.ToString(), "nothing", quotaBytes);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AnEmptyPlanIsRefused()
    {
        var response = await SetPlanAsync(Identities.Frodo.ToString(), "  ");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AnUnknownTenantIsNotFound()
    {
        var response = await SetPlanAsync("no-such-tenant.dotyou.cloud", "veterans");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private Task<HttpResponseMessage> SetPlanAsync(string domain, string planId, long? quotaBytes = null,
        bool? publicWebPresence = null) =>
        TestAdminApi.SetPlanAsync(Host, domain, planId, quotaBytes, publicWebPresence);
}
