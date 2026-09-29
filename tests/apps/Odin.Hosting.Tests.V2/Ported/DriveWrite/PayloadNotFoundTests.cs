using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/DirectDrivePayload_Notfound_Tests</c>. A file that really does
/// have payloads is asked for a payload key it does not have: the answer must be <c>404</c>, not a
/// <c>500</c> and not a silent empty body. The guest row is the counterpoint — a write-only guest
/// never gets far enough to learn whether the key exists, so it is refused at <c>403</c> instead.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> payload endpoint through the in-process host via
/// <see cref="UniversalDriveApiClient"/>, reached through <c>caller.V1.Drive</c> / <c>owner.V1.Drive</c>.
///
/// The seed is not gated on <c>expected == OK</c>: the guest row's refusal is the interesting half of
/// the matrix, and it is only meaningful against a file that genuinely has payloads.
///
/// Note the ordering shift <see cref="V2Fixture.SetupCallerWithOwner"/> imposes — the original created
/// the drive, uploaded as owner, and only then called <c>callerContext.Initialize</c>, whereas the
/// caller's grants now exist before the owner's seed upload. Checked: no assertion here depends on
/// that order.
/// </remarks>
[TestFixture]
public class PayloadNotFoundTests : V2Fixture
{
    public static IEnumerable<object[]> PayloadNotFoundCases()
    {
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.Forbidden];
        yield return [CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.NotFound];
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.NotFound];
    }

    [Test, TestCaseSource(nameof(PayloadNotFoundCases))]
    public async Task GetPayloadUsingValidPayloadKeyButPayloadDoesNotExistReturns404(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDriveClient = owner.V1.Drive;

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var testPayloads = new List<TestPayloadDefinition>()
        {
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1(),
            SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2()
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await ownerDriveClient.UploadNewFile(spec.TargetDrive, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.That(response.IsSuccessStatusCode, Is.True);
        var uploadResult = response.Content;
        Assert.That(uploadResult, Is.Not.Null);

        // get the file header
        var getHeaderResponse = await ownerDriveClient.GetFileHeader(uploadResult.File);
        Assert.That(getHeaderResponse.IsSuccessStatusCode, Is.True);
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(2));

        var callerDriveClient = caller.V1.Drive;

        // now that we know we have a valid file with a few payloads
        var getRandomPayload = await callerDriveClient.GetPayload(uploadResult.File, "r3nd0m09");
        Assert.That(getRandomPayload.StatusCode, Is.EqualTo(expected));
    }
}
