using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.Base.Cdn;
using Odin.Hosting.Controllers.OwnerToken.Cdn;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Optimization.Cdn;

namespace Odin.Hosting.Tests.V2.Ported.StaticFiles;

/// <summary>
/// Port of <c>_Universal/DriveTests/StaticFiles/DrivePublishStaticFileContentTests_1</c> and
/// <c>_2</c>. Covers publishing static content to a file (query sections, payloads + thumbnails,
/// CORS header, section round-trip) and publishing the public profile card / image, each across the
/// caller matrix.
/// </summary>
/// <remarks>
/// These are V1 endpoints (<c>/api/owner/v1/optimization/cdn/...</c>) driven through the in-process
/// host: the V1-shaped <see cref="UniversalStaticFileApiClient"/> and
/// <see cref="UniversalDriveApiClient"/> are reused unchanged, resolved against the fixture's
/// <c>Factory</c> via <c>InProcessApiClientFactory</c>'s V1 path normalization.
///
/// The two original fixtures collapse into one: they split only to carry different
/// <c>TestCaseSource</c> matrices, which <see cref="CallerSpec"/> now expresses as a parameter.
/// Publishing is gated on <see cref="PermissionKeys.PublishStaticContent"/> rather than on the drive
/// grant, so the app rows pin both directions — with the key, and with the drive grant but no key.
/// </remarks>
[TestFixture]
public class PublishStaticFileTests : V2Fixture
{
    private const int SectionOneFileType = 100;
    private const int SectionTwoDataType = 888;

    /// <summary>
    /// Publishing requires <see cref="PermissionKeys.PublishStaticContent"/>. A guest never has it
    /// (the endpoint isn't in the guest surface at all, hence NotFound); an app without the key is
    /// Forbidden even though it holds ReadWrite on the drive; owner and a keyed app succeed.
    /// </summary>
    public static IEnumerable<object[]> PublishCases()
    {
        var secured = new Func<DriveSpec>(() => new DriveSpec(TargetDrive.NewTargetDrive(), "Some Drive",
            AllowAnonymousReads: false));

        yield return [CallerSpec.Guest(secured(), DrivePermission.Write), HttpStatusCode.NotFound];
        yield return [CallerSpec.App(secured(), DrivePermission.ReadWrite, Array.Empty<int>()), HttpStatusCode.Forbidden];
        yield return
        [
            CallerSpec.App(secured(), DrivePermission.ReadWrite, [PermissionKeys.PublishStaticContent]),
            HttpStatusCode.OK
        ];
        yield return [CallerSpec.Owner(secured()), HttpStatusCode.OK];
    }

    /// <summary>
    /// The profile-card / image endpoints answer NoContent rather than OK on success, and are not
    /// exposed to app or guest callers at all.
    /// </summary>
    public static IEnumerable<object[]> ProfileCases()
    {
        var secured = new Func<DriveSpec>(() => new DriveSpec(TargetDrive.NewTargetDrive(), "Some Drive",
            AllowAnonymousReads: false));

        yield return [CallerSpec.Guest(secured(), DrivePermission.Write), HttpStatusCode.NotFound];
        yield return [CallerSpec.App(secured(), DrivePermission.Write), HttpStatusCode.NotFound];
        yield return [CallerSpec.Owner(secured()), HttpStatusCode.NoContent];
    }

    [Test]
    [TestCaseSource(nameof(PublishCases))]
    public async Task CanPublishStaticFileContentWithThumbnails(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerDrive = new UniversalDriveApiClient(owner.Identity, owner.Factory);

        const int totalFilesInSectionOne = 2;
        await CreateAnonymousUnEncryptedFile(ownerDrive, spec.TargetDrive,
            fileType: SectionOneFileType, dataType: 0,
            jsonContent: OdinSystemSerializer.Serialize(new { content = "some content" }),
            tags: [Guid.NewGuid(), Guid.NewGuid()],
            payload: SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2());

        await CreateAnonymousUnEncryptedFile(ownerDrive, spec.TargetDrive,
            fileType: SectionOneFileType, dataType: 0,
            jsonContent: OdinSystemSerializer.Serialize(new { content = "some content" }),
            tags: [Guid.NewGuid()],
            payload: SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1());

        await CreateAnonymousUnEncryptedFile(ownerDrive, spec.TargetDrive,
            fileType: 0, dataType: SectionTwoDataType,
            jsonContent: OdinSystemSerializer.Serialize(new { content = "stuff" }),
            tags: [Guid.NewGuid()],
            payload: SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1());

        var staticFileClient = new UniversalStaticFileApiClient(caller.Identity, caller.Factory);

        var publishRequest = new PublishStaticFileRequest
        {
            Filename = "test-file.ok",
            Config = new StaticFileConfiguration { CrossOriginBehavior = CrossOriginBehavior.AllowAllOrigins },
            Sections =
            [
                new QueryParamSection
                {
                    Name = $"Section matching filetype ({SectionOneFileType})",
                    QueryParams = new FileQueryParamsV1
                    {
                        TargetDrive = spec.TargetDrive,
                        FileType = [SectionOneFileType]
                    },
                    ResultOptions = new SectionResultOptions
                    {
                        PayloadKeys = [WebScaffold.PAYLOAD_KEY],
                        ExcludePreviewThumbnail = false,
                        IncludeHeaderContent = true
                    }
                },
                new QueryParamSection
                {
                    Name = $"Files matching datatype {SectionTwoDataType}",
                    QueryParams = new FileQueryParamsV1
                    {
                        TargetDrive = spec.TargetDrive,
                        DataType = [SectionTwoDataType]
                    },
                    ResultOptions = new SectionResultOptions
                    {
                        ExcludePreviewThumbnail = false,
                        IncludeHeaderContent = false
                    }
                }
            ]
        };

        var response = await staticFileClient.Publish(publishRequest);
        Assert.That(response.StatusCode, Is.EqualTo(expected), $"Actual status code was {response.StatusCode}");

        if (expected != HttpStatusCode.OK)
        {
            return;
        }

        var pubResult = response.Content;
        Assert.That(pubResult, Is.Not.Null);
        Assert.That(pubResult!.Filename, Is.EqualTo(publishRequest.Filename));
        Assert.That(pubResult.SectionResults.Count, Is.EqualTo(publishRequest.Sections.Count));
        Assert.That(pubResult.SectionResults[0].Name, Is.EqualTo(publishRequest.Sections[0].Name));
        Assert.That(pubResult.SectionResults[0].FileCount, Is.EqualTo(totalFilesInSectionOne));

        var getFileResponse = await staticFileClient.GetStaticFile(publishRequest.Filename);
        Assert.That(getFileResponse.IsSuccessStatusCode, Is.True, getFileResponse.ReasonPhrase);
        Assert.That(getFileResponse.Content, Is.Not.Null);

        Assert.That(getFileResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var values), Is.True);
        Assert.That(values!.Single(), Is.EqualTo("*"));

        var json = await getFileResponse.Content.ReadAsStringAsync();
        var sectionOutputArray = OdinSystemSerializer.Deserialize<SectionOutput[]>(json);
        Assert.That(sectionOutputArray, Is.Not.Null);
        Assert.That(sectionOutputArray!.Length, Is.EqualTo(publishRequest.Sections.Count));
    }

    [Test]
    [TestCaseSource(nameof(ProfileCases))]
    public async Task CanPublishPublicProfileCard(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var staticFileClient = new UniversalStaticFileApiClient(caller.Identity, caller.Factory);

        const string expectedJson = "{name:'Sam'}";
        var response = await staticFileClient.PublishPublicProfileCard(new PublishPublicProfileCardRequest
        {
            ProfileCardJson = expectedJson
        });

        Assert.That(response.StatusCode, Is.EqualTo(expected), $"Actual status code was {response.StatusCode}");

        if (expected != HttpStatusCode.NoContent)
        {
            return;
        }

        // The original asserted the read-back under `expected == OK`, which this endpoint never
        // returns, so the block never ran. Read it back on the success path that does happen.
        var ownerStaticFiles = new UniversalStaticFileApiClient(owner.Identity, owner.Factory);
        var getResponse = await ownerStaticFiles.GetPublicProfileCard();
        Assert.That(getResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getResponse.ContentHeaders!.ContentType!.MediaType, Is.EqualTo(MediaTypeNames.Application.Json));
        Assert.That(await getResponse.Content!.ReadAsStringAsync(), Is.EqualTo(expectedJson));
    }

    [Test]
    [TestCaseSource(nameof(ProfileCases))]
    public async Task CanPublishPublicProfileImage(CallerSpec spec, HttpStatusCode expected)
    {
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var staticFileClient = new UniversalStaticFileApiClient(caller.Identity, caller.Factory);

        var expectedImage = TestMedia.ThumbnailBytes300;
        var response = await staticFileClient.PublishPublicProfileImage(new PublishPublicProfileImageRequest
        {
            Image64 = expectedImage.ToBase64(),
            ContentType = MediaTypeNames.Image.Jpeg
        });

        Assert.That(response.StatusCode, Is.EqualTo(expected), $"Actual status code was {response.StatusCode}");

        if (expected != HttpStatusCode.NoContent)
        {
            return;
        }

        // Same as above: the original's read-back was gated on OK and never ran.
        var ownerStaticFiles = new UniversalStaticFileApiClient(owner.Identity, owner.Factory);
        var getResponse = await ownerStaticFiles.GetPublicProfileImage();
        Assert.That(getResponse.IsSuccessStatusCode, Is.True);
        Assert.That(getResponse.ContentHeaders!.ContentType!.MediaType, Is.EqualTo(MediaTypeNames.Image.Jpeg));
        var bytes = await getResponse.Content!.ReadAsByteArrayAsync();
        Assert.That(ByteArrayUtil.EquiByteArrayCompare(expectedImage, bytes), Is.True);
    }

    private static async Task CreateAnonymousUnEncryptedFile(
        UniversalDriveApiClient driveClient,
        TargetDrive targetDrive,
        int fileType,
        int dataType,
        string jsonContent,
        List<Guid> tags,
        TestPayloadDefinition payload)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new UploadAppFileMetaData
            {
                Tags = tags,
                Content = jsonContent,
                FileType = fileType,
                DataType = dataType,
                PreviewThumbnail = payload.PreviewThumbnail
            },
            AccessControlList = new AccessControlList { RequiredSecurityGroup = SecurityGroupType.Anonymous }
        };

        var uploadManifest = new UploadManifest
        {
            PayloadDescriptors = [payload.ToPayloadDescriptor()]
        };

        var response = await driveClient.UploadNewFile(targetDrive, fileMetadata, uploadManifest, [payload]);
        Assert.That(response.IsSuccessStatusCode, Is.True, $"upload failed: {response.StatusCode}");
        Assert.That(response.Content!.File.FileId, Is.Not.EqualTo(Guid.Empty));
    }
}
