#if RUN_S3_TESTS
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Admin.Tenants;
using Odin.Services.Drives;
using Testcontainers.Minio;

namespace Odin.Hosting.Tests.V2.S3;

/// <summary>
/// The one fixture whose host keeps payloads in S3 (#1782): every other host keeps them on local disk.  It brings back
/// what the port of <c>AdminControllerTest</c> lost, the assertion that a tenant's payload path is an S3 location, and
/// shows an upload with a payload works against S3 at all.
/// </summary>
/// <remarks>
/// Compiled only with <c>RUN_S3_TESTS</c>, which CI's Release build defines, as <c>WebScaffold</c>'s S3 runs are: it
/// needs Docker for the S3 container (the image <c>WebScaffold</c> uses).  The S3 settings go in through
/// <see cref="V2Fixture.ConfigOverrides"/>, so they reach this host only; NUnit reads them in the base class's one-time
/// setup, before this class's own, so the container is started on that first read.
/// </remarks>
[TestFixture]
public class S3PayloadStorageTests : V2Fixture
{
    private const string BucketName = "odin-payloads";

    private MinioContainer? _s3;

    protected override string[] HostIdentities => [Identities.Frodo];

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides
    {
        get
        {
            var s3 = StartS3();
            return new Dictionary<string, string?>(TestAdminApi.Config())
            {
                ["S3Storage:Enabled"] = "true",
                ["S3Storage:AccessKey"] = s3.GetAccessKey(),
                ["S3Storage:SecretAccessKey"] = s3.GetSecretKey(),
                ["S3Storage:ServiceUrl"] = s3.GetConnectionString(),
                ["S3Storage:Region"] = "meh",
                ["S3Storage:ForcePathStyle"] = "true",
                ["S3Payload:Enabled"] = "true",
                ["S3Payload:BucketName"] = BucketName,
            };
        }
    }

    private MinioContainer StartS3()
    {
        if (_s3 == null)
        {
            _s3 = new MinioBuilder("rustfs/rustfs:1.0.0")
                .WithUsername("minioadmin")
                .WithPassword("minioadmin123")
                .Build();
            _s3.StartAsync().GetAwaiter().GetResult();
        }

        return _s3;
    }

    [OneTimeTearDown]
    public async Task StopS3()
    {
        if (_s3 != null)
        {
            await _s3.DisposeAsync();
        }
    }

    [Test]
    public async Task ATenantsPayloadPath_IsItsS3Location_AndAPayloadUploadsThere()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await frodo.Admin.CreateDrive(drive, "payloads", allowAnonymousReads: false);
        await AppFileUploads.UploadEncryptedAsync(frodo, drive, SampleMetadataData.Create(fileType: 200), "a payload in S3");

        using var admin = TestAdminApi.CreateClient(Host);
        var response = await admin.SendAsync(
            TestAdminApi.Request(System.Net.Http.HttpMethod.Get, $"tenants/{Identities.Frodo}?include-payload=true"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var tenant = OdinSystemSerializer.Deserialize<TenantModel>(await response.Content.ReadAsStringAsync())!;

        Assert.That(tenant.PayloadPath, Is.EqualTo(Path.Combine(_s3!.GetConnectionString(), BucketName, tenant.Id)));
    }
}
#endif
