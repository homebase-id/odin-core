#nullable enable
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
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
    private const string PayloadKey = "quotapay1";

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

        var response = await owner.V1.Drive.UploadNewMetadata(drive, SampleMetadataData.Create(fileType: 100));

        Assert.That(response.IsSuccessStatusCode, Is.True, $"header upload: {response.StatusCode}");
    }

    [Test]
    public async Task OverQuotaAPayloadIsRefused()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = await CreateDriveAsync(owner);
        await StoreSomethingAsync(owner, drive);

        var manifest = new UploadManifest
        {
            PayloadDescriptors = [new() { Iv = null, PayloadKey = PayloadKey, Thumbnails = [] }]
        };
        var payloads = new List<TestPayloadDefinition>
        {
            new()
            {
                Iv = null, Key = PayloadKey, ContentType = "application/x-binary",
                Content = "over quota".ToUtf8ByteArray(), Thumbnails = []
            }
        };

        var response = await owner.V1.Drive.UploadNewFile(drive, SampleMetadataData.CreateWithContent(200, "x"), manifest, payloads);

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
        var client = app.Factory.CreateHttpClient(app.Identity, out var sharedSecret);
        var appResponse = await RefitCreator.RestServiceFor<IStorageUsageClient>(client, sharedSecret).GetUsage();

        Assert.That(appResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(ownerUsage.QuotaBytes, Is.EqualTo(1));
        Assert.That(ownerUsage.UsedBytes, Is.GreaterThan(0));
        Assert.That(ownerUsage.IsOverQuota, Is.True);
        Assert.That(appResponse.Content, Is.EqualTo(ownerUsage));
    }

    // A header is enough to put the identity over a 1-byte quota
    private static async Task StoreSomethingAsync(OwnerSession owner, TargetDrive drive)
    {
        var response = await owner.V1.Drive.UploadNewMetadata(drive, SampleMetadataData.Create(fileType: 100));
        Assert.That(response.IsSuccessStatusCode, Is.True, $"header upload: {response.StatusCode}");
    }

    private static async Task<TargetDrive> CreateDriveAsync(OwnerSession owner)
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
