using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

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
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Read), HttpStatusCode.Forbidden];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.OK];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
    }

    [Test, TestCaseSource(nameof(WriteCases))]
    public async Task CanUploadMetadataWithoutPayloads(CallerSpec spec, HttpStatusCode expected)
    {
        var caller = await SetupCaller(spec);
        var metadata = SampleMetadataData.Create(fileType: 100);

        var response = await caller.Drives.Writer.UploadNewMetadata(spec.TargetDrive.Alias, metadata);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }
}
