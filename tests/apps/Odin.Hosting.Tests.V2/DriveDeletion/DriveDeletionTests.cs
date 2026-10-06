using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.DriveDeletion;

/// <summary>
/// The owner's bulk hard delete and "empty drive" (#1869): local, no tombstones, payloads gone from storage.
/// </summary>
[TestFixture]
public class DriveDeletionTests : V2Fixture
{
    private const string PayloadKey = "pyld1234";

    [Test]
    public async Task HardDeleteFileIdBatchRemovesTheListedFilesAndTheirPayloads()
    {
        var owner = await LoginAsOwner();
        var drive = await CreateDriveAsync(owner);
        var files = new List<ExternalFileIdentifier>();
        for (var i = 0; i < 3; i++)
        {
            files.Add(await UploadAsync(owner, drive, $"file {i}"));
        }

        var response = await owner.RefitFor<IRefitOwnerDriveDeletion>().HardDeleteFileIdBatch(new DeleteFileIdBatchRequest
        {
            Requests = files.Take(2).Select(f => new DeleteFileRequest { File = f }).ToList()
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        foreach (var deleted in files.Take(2))
        {
            Assert.That((await owner.V1.Drive.GetFileHeader(deleted)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((await owner.V1.Drive.GetPayload(deleted, PayloadKey)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        Assert.That((await owner.V1.Drive.GetFileHeader(files[2])).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task EmptyDriveDeletesEveryFileAndKeepsTheDrive()
    {
        var owner = await LoginAsOwner();
        var drive = await CreateDriveAsync(owner);
        var other = await CreateDriveAsync(owner);
        var files = new List<ExternalFileIdentifier>
        {
            await UploadAsync(owner, drive, "one"),
            await UploadAsync(owner, drive, "two")
        };
        var untouched = await UploadAsync(owner, other, "elsewhere");

        var response = await owner.RefitFor<IRefitOwnerDriveDeletion>().EmptyDrive(new TargetDriveRequest { TargetDrive = drive });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        foreach (var file in files)
        {
            Assert.That((await owner.V1.Drive.GetFileHeader(file)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        Assert.That(Directory.Exists(PayloadDirectory(owner, drive)), Is.False, "payload directory left behind");
        Assert.That((await owner.V1.Drive.GetFileHeader(untouched)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await owner.V1.Drive.GetPayload(untouched, PayloadKey)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // the drive still works
        var again = await UploadAsync(owner, drive, "after");
        var payload = await owner.V1.Drive.GetPayload(again, PayloadKey);
        Assert.That(payload.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task EmptyDriveRefusesASystemDrive()
    {
        var owner = await LoginAsOwner();

        var response = await owner.RefitFor<IRefitOwnerDriveDeletion>()
            .EmptyDrive(new TargetDriveRequest { TargetDrive = BuiltinDrives.Protected[0] });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private static async Task<TargetDrive> CreateDriveAsync(OwnerSession owner)
    {
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "deletable", allowAnonymousReads: false);
        return drive;
    }

    private string PayloadDirectory(OwnerSession owner, TargetDrive drive) =>
        Host.GetTenantScope(owner.Identity.DomainName).Resolve<TenantContext>().TenantPathManager
            .GetDrivePayloadPath(drive.Alias);

    private static async Task<ExternalFileIdentifier> UploadAsync(OwnerSession owner, TargetDrive drive, string content)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new() { Content = content, FileType = 200 },
            AccessControlList = AccessControlList.OwnerOnly
        };

        var manifest = new UploadManifest
        {
            PayloadDescriptors = [new() { Iv = null, PayloadKey = PayloadKey, Thumbnails = [] }]
        };

        var payloads = new List<TestPayloadDefinition>
        {
            new()
            {
                Iv = null,
                Key = PayloadKey,
                ContentType = "application/x-binary",
                Content = content.ToUtf8ByteArray(),
                Thumbnails = []
            }
        };

        var response = await owner.V1.Drive.UploadNewFile(drive, fileMetadata, manifest, payloads);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        return response.Content!.File;
    }
}
