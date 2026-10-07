using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.Management;
using Odin.Services.JobManagement;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.DriveDeletion;

/// <summary>
/// The owner's drive deletion (#1869): bulk hard delete, and emptying or deleting a drive -- archived and
/// non-system only. Local, no tombstones, payloads gone from storage; a deleted drive leaves no grant naming it,
/// so a drive re-created with the same alias, as an app reinstall does, inherits nothing.
/// </summary>
[TestFixture]
public class DriveDeletionTests : V2Fixture
{
    private const string PayloadKey = "pyld1234";

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    /// <summary>
    /// Jobs live in the system database, which the per-test reset leaves alone, so a purge job a failed test never
    /// ran would otherwise turn up in the next one.
    /// </summary>
    [SetUp]
    public async Task ClearJobs()
    {
        var jobManager = Host.Server.Services.GetRequiredService<IJobManager>();
        foreach (var identity in HostIdentities)
        {
            await jobManager.DeleteJobsByIdentityIdAsync(IdentityId(identity));
        }
    }

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
        await owner.Admin.SetArchiveFlag(drive, archived: true);

        Assert.That(await EmptyAsync(owner, drive), Is.EqualTo(HttpStatusCode.Accepted));
        await RunPurgeAsync(owner);

        foreach (var file in files)
        {
            Assert.That((await owner.V1.Drive.GetFileHeader(file)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        Assert.That(PayloadFiles(owner, drive), Is.Empty, "payload files left behind");
        Assert.That((await owner.V1.Drive.GetFileHeader(untouched)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await owner.V1.Drive.GetPayload(untouched, PayloadKey)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // the drive still works once restored
        await owner.Admin.SetArchiveFlag(drive, archived: false);
        var again = await UploadAsync(owner, drive, "after");
        Assert.That((await owner.V1.Drive.GetPayload(again, PayloadKey)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ASystemDriveCanBeNeitherEmptiedNorDeleted()
    {
        var owner = await LoginAsOwner();
        var system = BuiltinDrives.Protected[0];

        Assert.That(await EmptyAsync(owner, system), Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await DeleteAsync(owner, system), Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AnActiveDriveIsNeitherEmptiedNorDeleted()
    {
        var owner = await LoginAsOwner();
        var drive = await CreateDriveAsync(owner);
        var file = await UploadAsync(owner, drive, "kept");

        Assert.That(await EmptyAsync(owner, drive), Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await DeleteAsync(owner, drive), Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await owner.V1.Drive.GetFileHeader(file)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await owner.Admin.GetDrives()).Any(d => d.TargetDriveInfo == drive), Is.True);
    }

    [Test]
    public async Task AnArchivedDriveIsDeletedAndItsAliasCanBeReused()
    {
        var owner = await LoginAsOwner();
        var drive = await CreateDriveAsync(owner);
        var file = await UploadAsync(owner, drive, "content");
        await owner.Admin.SetArchiveFlag(drive, archived: true);

        Assert.That(await DeleteAsync(owner, drive), Is.EqualTo(HttpStatusCode.Accepted));

        // gone at once, but its alias is held until the files are
        Assert.That((await owner.Admin.GetDrives()).Any(d => d.TargetDriveInfo == drive), Is.False);
        var tooSoon = await owner.RefitFor<IRefitDriveManagement>().CreateDrive(new CreateDriveRequest
        {
            TargetDrive = drive, Name = "too soon", Metadata = "", AllowAnonymousReads = false
        });
        Assert.That(tooSoon.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "the alias was reusable before the purge");

        await RunPurgeAsync(owner);
        Assert.That(Directory.Exists(Paths(owner).GetDrivePath(drive.Alias)), Is.False, "drive directory left behind");

        // the alias is free again, and the new drive starts empty
        await owner.Admin.CreateDrive(drive, "reborn", allowAnonymousReads: false);
        Assert.That((await owner.V1.Drive.GetFileHeader(file)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task NoGrantNamesADeletedDrive()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // Sam is connected through a circle of Frodo's that grants write on the drive
        var drive = await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write, allowAnonymousReads: false);
        Assert.That(await SamHasGrantOnAsync(frodo, sam, drive), Is.True, "arrange: Sam holds no grant on the drive");

        // and an app holds it, for itself and for its circles' members
        var appId = Guid.NewGuid();
        var onDrive = new PermissionSetGrantRequest
        {
            Drives = [new DriveGrantRequest { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read } }]
        };
        await frodo.Admin.RegisterApp(appId, onDrive, circleMemberGrantRequest: onDrive);

        await frodo.Admin.SetArchiveFlag(drive, archived: true);
        Assert.That(await DeleteAsync(frodo, drive), Is.EqualTo(HttpStatusCode.Accepted));

        Assert.That(await SamHasGrantOnAsync(frodo, sam, drive), Is.False, "Sam's circle grant still names the drive");

        var circles = (await frodo.RefitFor<IRefitOwnerCircleDefinition>().GetCircleDefinitions(includeSystemCircle: true)).Content!;
        Assert.That(circles.Where(c => c.DriveGrants.Any(g => g.PermissionedDrive.Drive == drive)).Select(c => c.Name), Is.Empty,
            "a circle definition still grants the drive");

        var app = (await frodo.RefitFor<IRefitOwnerAppRegistration>().GetRegisteredApp(new GetAppRequest { AppId = appId })).Content!;
        Assert.That(app.Grant.DriveGrants.Any(g => g.PermissionedDrive.Drive == drive), Is.False, "the app's grant still names the drive");
        Assert.That(app.CircleMemberPermissionSetGrantRequest.Drives.Any(g => g.PermissionedDrive.Drive == drive), Is.False,
            "the app's circle-member grant still names the drive");
    }

    [Test]
    public async Task EmptySparesFilesUploadedAfterTheRequest()
    {
        var owner = await LoginAsOwner();
        var drive = await CreateDriveAsync(owner);
        var before = await UploadAsync(owner, drive, "before");
        await owner.Admin.SetArchiveFlag(drive, archived: true);

        Assert.That(await EmptyAsync(owner, drive), Is.EqualTo(HttpStatusCode.Accepted));
        await Task.Delay(5);
        var after = await UploadAsync(owner, drive, "after");
        await RunPurgeAsync(owner);

        Assert.That((await owner.V1.Drive.GetFileHeader(before)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await owner.V1.Drive.GetFileHeader(after)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await owner.V1.Drive.GetPayload(after, PayloadKey)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task APurgeWorksThroughADriveInBatches()
    {
        var owner = await LoginAsOwner();
        var drive = await CreateDriveAsync(owner);
        var files = new List<ExternalFileIdentifier>();
        for (var i = 0; i < 5; i++)
        {
            files.Add(await UploadAsync(owner, drive, $"file {i}"));
        }

        await owner.Admin.SetArchiveFlag(drive, archived: true);
        var batchSize = DrivePurgeJob.BatchSize;
        try
        {
            DrivePurgeJob.BatchSize = 2;
            Assert.That(await EmptyAsync(owner, drive), Is.EqualTo(HttpStatusCode.Accepted));

            Assert.That(await RunPurgeAsync(owner), Is.EqualTo(3), "5 files in batches of 2 is 3 runs");
        }
        finally
        {
            DrivePurgeJob.BatchSize = batchSize;
        }

        foreach (var file in files)
        {
            Assert.That((await owner.V1.Drive.GetFileHeader(file)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }

    private static async Task<HttpStatusCode> EmptyAsync(OwnerSession owner, TargetDrive drive) =>
        (await owner.RefitFor<IRefitDriveManagement>().EmptyDrive(new TargetDriveRequest { TargetDrive = drive })).StatusCode;

    private static async Task<HttpStatusCode> DeleteAsync(OwnerSession owner, TargetDrive drive) =>
        (await owner.RefitFor<IRefitDriveManagement>().DeleteDrive(new TargetDriveRequest { TargetDrive = drive })).StatusCode;

    private static async Task<bool> SamHasGrantOnAsync(OwnerSession frodo, OwnerSession sam, TargetDrive drive)
    {
        RedactedIdentityConnectionRegistration info = (await frodo.Connections.GetConnectionInfo(sam.Identity)).Content!;
        return info.AccessGrant.CircleGrants.SelectMany(c => c.DriveGrants).Any(g => g.PermissionedDrive.Drive == drive);
    }

    /// <summary>
    /// Runs the owner's drive purge job until it is done -- the test host does not run jobs on its own -- and
    /// returns how many runs it took. A finished purge job deletes itself.
    /// </summary>
    private async Task<int> RunPurgeAsync(OwnerSession owner)
    {
        var jobManager = Host.Server.Services.GetRequiredService<IJobManager>();

        for (var runs = 1; runs <= 20; runs++)
        {
            var jobs = (await jobManager.GetJobsByIdentityIdAsync(IdentityId(owner.Identity.DomainName)))
                .Where(j => j.jobType == DrivePurgeJob.JobTypeId.ToString())
                .ToList();
            if (jobs.Count == 0)
            {
                return runs - 1;
            }

            foreach (var job in jobs)
            {
                await jobManager.RunJobNowAsync(job.id, CancellationToken.None);
            }
        }

        Assert.Fail("the purge job did not finish");
        return -1;
    }

    private Guid IdentityId(string domain) =>
        Host.GetTenantScope(domain).Resolve<TenantContext>().DotYouRegistryId;

    private string[] PayloadFiles(OwnerSession owner, TargetDrive drive)
    {
        var directory = Paths(owner).GetDrivePayloadPath(drive.Alias);
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories) : [];
    }

    private TenantPathManager Paths(OwnerSession owner) =>
        Host.GetTenantScope(owner.Identity.DomainName).Resolve<TenantContext>().TenantPathManager;

    private static async Task<TargetDrive> CreateDriveAsync(OwnerSession owner)
    {
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "deletable", allowAnonymousReads: false);
        return drive;
    }

    private static async Task<ExternalFileIdentifier> UploadAsync(OwnerSession owner, TargetDrive drive, string content)
    {
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

        var response = await owner.V1.Drive.UploadNewFile(drive, SampleMetadataData.CreateWithContent(200, content), manifest, payloads);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        return response.Content!.File;
    }
}
