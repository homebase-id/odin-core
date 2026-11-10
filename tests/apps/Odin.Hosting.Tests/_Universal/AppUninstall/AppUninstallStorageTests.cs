using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Services.JobManagement;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;
using Odin.Test.Helpers;

namespace Odin.Hosting.Tests._Universal.AppUninstall;

/// <summary>
/// Uninstalling an app that owns two drives, each holding files with payloads and thumbnails, leaves nothing of
/// them in the payload store: every object the files had is gone, and so is everything under each drive.
/// Asked of the store itself, so it holds on disk, and on S3 when the build defines RUN_S3_TESTS (the scaffold
/// then runs a RustFS container and the identity's payloads live there).
/// </summary>
[Explicit("Run locally, once plain and once with RUN_S3_TESTS defined, to check both payload stores")]
public class AppUninstallStorageTests
{
    private const int FilesPerDrive = 3;
    private WebScaffold _scaffold = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _scaffold = new WebScaffold(GetType().Name);
        _scaffold.RunBeforeAnyTests(testIdentities: [TestIdentities.Frodo]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [Test]
    public async Task UninstallingAnAppLeavesNothingOfItsDrivesPayloadsInTheStore()
    {
        var identity = TestIdentities.Frodo;
        var owner = _scaffold.CreateOwnerApiClientRedux(identity);
        var appId = Guid.NewGuid();
        var registered = await owner.AppManager.RegisterApp(appId, new PermissionSetGrantRequest());
        Assert.That(registered.IsSuccessStatusCode, Is.True, $"register the app: {registered.StatusCode}");

        var drives = new[] { TargetDrive.NewTargetDrive(), TargetDrive.NewTargetDrive() };
        var objects = new List<string>();
        foreach (var drive in drives)
        {
            await CreateAppDriveAsync(identity, drive, appId);
            for (var i = 0; i < FilesPerDrive; i++)
            {
                objects.AddRange(await UploadAsync(owner, Paths(identity), drive, $"file {i}"));
            }
        }

        var paths = Paths(identity);
        await using (var scope = TenantScope(identity))
        {
            var store = scope.Resolve<LongTermPayloadStore>();
            Assert.That(objects, Has.Count.EqualTo(drives.Length * FilesPerDrive * 4),
                "precondition: two payloads with a thumbnail each, per file");
            foreach (var path in objects)
            {
                Assert.That(await store.ExistsAsync(path), Is.True, $"precondition: stored before the uninstall: {path}");
            }
            Console.WriteLine($"Payload store: {(store.GetS3Location(paths.GetDrivePath(drives[0].Alias)) == null ? "disk" : "S3")}");
        }

        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var sharedSecret);
        var uninstall = await RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, sharedSecret)
            .Uninstall(new UninstallAppRequest { AppId = appId, DeleteOwnedCirclesAndDrives = true });
        Assert.That(uninstall.StatusCode, Is.EqualTo(HttpStatusCode.OK), "uninstall");

        Assert.That(await PurgesDoneAsync(identity), Is.True, "the purge did not finish");

        await using (var scope = TenantScope(identity))
        {
            var store = scope.Resolve<LongTermPayloadStore>();
            foreach (var path in objects)
            {
                Assert.That(await store.ExistsAsync(path), Is.False, $"left in the store after the uninstall: {path}");
            }

            foreach (var drive in drives)
            {
                var left = await EverythingUnderAsync(store, paths.GetDrivePath(drive.Alias));
                Assert.That(left, Is.Empty, $"left under drive {drive.Alias}: {string.Join(", ", left)}");
            }
        }
    }

    // Through the owner console's drive call, naming the app as the drive's owner
    private async Task CreateAppDriveAsync(TestIdentity identity, TargetDrive drive, Guid appId)
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var sharedSecret);
        var response = await RefitCreator.RestServiceFor<IRefitDriveManagement>(client, sharedSecret).CreateDrive(
            new CreateDriveRequest
            {
                TargetDrive = drive,
                Name = "app drive",
                Metadata = "",
                AppId = appId,
                DriveSlug = TestSlug.New(),
                DriveTypeSlug = TestSlug.DriveType
            });
        Assert.That(response.IsSuccessStatusCode, Is.True, $"create the app's drive: {response.StatusCode}");
    }

    // A file with two payloads, each with a thumbnail; answers the store path of each of the four objects
    private static async Task<List<string>> UploadAsync(ApiClient.Owner.OwnerApiClientRedux owner, TenantPathManager paths,
        TargetDrive drive, string content)
    {
        var definitions = new List<TestPayloadDefinition>
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(),
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2()
        };
        var metadata = new UploadFileMetadata
        {
            IsEncrypted = false,
            AppData = new UploadAppFileMetaData { Content = content },
            AccessControlList = AccessControlList.OwnerOnly
        };
        var upload = await owner.DriveRedux.UploadNewFile(drive, metadata,
            new UploadManifest { PayloadDescriptors = definitions.ToPayloadDescriptorList().ToList() }, definitions);
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"upload: {upload.StatusCode}");

        var file = upload.Content!.File;
        var header = await owner.DriveRedux.GetFileHeader(file);
        Assert.That(header.IsSuccessStatusCode, Is.True, $"read the header: {header.StatusCode}");

        var objects = new List<string>();
        foreach (var payload in header.Content!.FileMetadata.Payloads)
        {
            objects.Add(paths.GetPayloadDirectoryAndFileName(drive.Alias, file.FileId, payload.Key,
                payload.Uid));
            objects.AddRange(payload.Thumbnails.Select(t => paths.GetThumbnailDirectoryAndFileName(
                drive.Alias, file.FileId, payload.Key, payload.Uid, t.PixelWidth, t.PixelHeight)));
        }

        return objects;
    }

    // What is left under a drive: its directory's files on disk, the objects under its prefix on S3
    private async Task<List<string>> EverythingUnderAsync(LongTermPayloadStore store, string drivePath)
    {
        var s3 = store.GetS3Location(drivePath);
        if (s3 == null)
        {
            return Directory.Exists(drivePath)
                ? Directory.GetFiles(drivePath, "*", SearchOption.AllDirectories).ToList()
                : [];
        }

        var (bucket, key) = s3.Value;
        var listed = await _scaffold.Services.GetRequiredService<IAmazonS3>().ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = bucket,
            Prefix = key.TrimEnd('/') + "/"
        });
        return (listed.S3Objects ?? []).Select(o => o.Key).ToList();
    }

    // The host's job runner works through the purges the uninstall scheduled; wait until none is left
    private async Task<bool> PurgesDoneAsync(TestIdentity identity)
    {
        var jobs = _scaffold.Services.GetRequiredService<IJobManager>();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var purges = (await jobs.GetJobsByIdentityIdAsync(IdOf(identity)))
                .Where(j => j.jobType == DrivePurgeJob.JobTypeId.ToString());
            if (!purges.Any())
            {
                return true;
            }

            await Task.Delay(250);
        }

        return false;
    }

    private TenantPathManager Paths(TestIdentity identity) =>
        new(_scaffold.Services.GetRequiredService<OdinConfiguration>(), IdOf(identity));

    private Guid IdOf(TestIdentity identity) =>
        _scaffold.Services.GetRequiredService<IIdentityRegistry>().ResolveId(identity.OdinId.DomainName)!.Value;

    private ILifetimeScope TenantScope(TestIdentity identity) => _scaffold.Services.GetRequiredService<IMultiTenantContainer>()
        .GetTenantScope(identity.OdinId.DomainName).BeginLifetimeScope(nameof(AppUninstallStorageTests));
}
