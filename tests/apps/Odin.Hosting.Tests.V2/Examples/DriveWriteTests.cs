using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Examples;

/// <summary>
/// Port of <c>_V2/Tests/Drive/WriteFileTests/DiretDriveWriteNewFileTestsV2.CanUploadMetadataDataWithoutPayloads</c>.
/// Confirms the V2 drive-write pipeline works over the in-process router across the Owner / App / Guest
/// matrix, including the encrypted-multipart upload path and ACL enforcement (Read-only callers get 403,
/// Write callers get 200).
/// </summary>
[TestFixture]
public class DriveWriteTests : V2Fixture
{
    public static IEnumerable<object[]> WriteCases()
    {
        yield return [CallerSpec.Guest(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(TargetDrive.NewTargetDrive(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.App(TargetDrive.NewTargetDrive(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(TargetDrive.NewTargetDrive()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task CanUploadMetadataWithoutPayloads(CallerSpec spec, HttpStatusCode expected)
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        await owner.Admin.CreateDrive(spec.TargetDrive, "Test Drive 001");
        var caller = await spec.Build(owner, Host);

        var writer = new DriveWriterV2Client(caller.Identity, caller.Factory);
        var metadata = SampleMetadataData.Create(fileType: 100);
        var response = await writer.UploadNewMetadata(spec.TargetDrive.Alias, metadata);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }
}
