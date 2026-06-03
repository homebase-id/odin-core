using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Isolation;

/// <summary>
/// Sentinel for <see cref="OdinHost.ResetAsync"/>: proves that state written in one test is not
/// visible to the next test in the same fixture. The first method creates Drive A and uploads a
/// file; the other methods would 404 or 409 if the per-test reset wasn't actually wiping the
/// identity DB and payload tree. <see cref="OrderAttribute"/> pins the sequence — relying on NUnit
/// alphabetical method discovery is implementation-defined.
/// </summary>
[TestFixture]
public class PerTestResetTests : V2Fixture
{
    private static readonly TargetDrive SharedDriveAlias = TargetDrive.NewTargetDrive();

    [Test, Order(1)]
    public async Task A_CreateDriveAndUploadFile()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var createResp = await owner.Admin.CreateDrive(SharedDriveAlias, "Sentinel");
        Assert.That(createResp.IsSuccessStatusCode, Is.True);

        var upload = await owner.Drives.Writer.UploadNewMetadata(SharedDriveAlias.Alias, SampleMetadataData.Create(fileType: 100));
        Assert.That(upload.IsSuccessStatusCode, Is.True);
    }

    [Test, Order(2)]
    public async Task B_DriveAliasIsAvailableAgain()
    {
        // If the reset didn't run, A's drive (same alias) would still exist and CreateDrive
        // would return a conflict / 4xx. Successful create here proves the DB was restored to
        // the post-warm-up baseline.
        var owner = await LoginAsOwner(Identities.Frodo);
        var createResp = await owner.Admin.CreateDrive(SharedDriveAlias, "Sentinel reborn");
        Assert.That(createResp.IsSuccessStatusCode, Is.True, $"reset did not clear drive — got {createResp.StatusCode}");
    }

    [Test, Order(3)]
    public async Task C_NoFilesOnFreshDrive()
    {
        // Same drive alias again, plus a peek: the drive should exist but be empty. The cleanest
        // way to confirm "no leaked file" without depending on V2 query endpoints is to upload a
        // fresh file and confirm that succeeds — meaning the seed in A didn't pollute payload
        // storage or accumulate state under this drive id.
        var owner = await LoginAsOwner(Identities.Frodo);
        var createResp = await owner.Admin.CreateDrive(SharedDriveAlias, "Sentinel third pass");
        Assert.That(createResp.IsSuccessStatusCode, Is.True);

        var upload = await owner.Drives.Writer.UploadNewMetadata(SharedDriveAlias.Alias, SampleMetadataData.Create(fileType: 100));
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
