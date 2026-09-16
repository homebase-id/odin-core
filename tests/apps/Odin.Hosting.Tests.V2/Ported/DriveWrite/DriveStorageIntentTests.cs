using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>AppAPI/Drive/DriveStorageIntentTests</c>. <c>StorageIntent.MetadataOnly</c> as an app:
/// a metadata-only update rewrites the content and bumps the version tag, and it does so even when
/// the caller claims the payload changed — the file ends up with no payloads either way.
/// </summary>
/// <remarks>
/// The original's <c>CreateApp</c> helper — owner creates a drive (anonymous reads off) and
/// registers an app with <see cref="DrivePermission.All"/> on it and
/// <c>new PermissionSet(PermissionKeys.All)</c> — is exactly
/// <see cref="CallerSpec.App(DriveSpec, DrivePermission, System.Collections.Generic.IReadOnlyList{int})"/>.
/// One live caller, so no matrix and plain <c>[Test]</c> methods.
/// <para>
/// <c>AppDriveApiClient.UploadFile(drive, metadata, "")</c> means "no payload", so it becomes
/// <c>UploadNewMetadata</c>; <c>UpdateMetadata</c> / <c>UpdateMetadataRaw</c> become
/// <c>UpdateExistingMetadata</c>, which sends the same instruction set (<c>OverwriteFileId</c>,
/// <c>StorageIntent.MetadataOnly</c>, no <c>EncryptedKeyHeader</c>) and the same version tag the
/// original assigned to <c>fileMetadata.VersionTag</c> by hand. The difference between the two
/// original methods was only that one asserted success internally; here both calls return the
/// response and the test asserts on it.
/// </para>
/// </remarks>
[TestFixture]
public class DriveStorageIntentTests : V2Fixture
{
    /// <summary>The app the original's <c>CreateApp</c> registered.</summary>
    private static CallerSpec SampleApp() =>
        CallerSpec.App(new DriveSpec(TargetDrive.NewTargetDrive(), "Some app Drive 1", AllowAnonymousReads: false),
            DrivePermission.All, PermissionKeys.All);

    [Test]
    public async Task CanUpdateMetadataOnly_StorageIntentMedata()
    {
        var spec = SampleApp();
        var appApiClient = await SetupCaller(spec);
        var targetDrive = spec.TargetDrive;

        var content1 = OdinSystemSerializer.Serialize(new { data = "nom nom nom" });
        var content2 = OdinSystemSerializer.Serialize(new { data = "chomp chomp chomp" });

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            AppData = new()
            {
                FileType = 101,
                Content = content1
            },
            IsEncrypted = false,
            AccessControlList = AccessControlList.Connected
        };

        //upload normal
        var uploadResponse = await appApiClient.V1.Drive.UploadNewMetadata(targetDrive, fileMetadata);
        Assert.That(uploadResponse.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadResponse.Content!;

        var firstHeader = (await appApiClient.V1.Drive.GetFileHeader(uploadResult.File)).Content;
        Assert.That(firstHeader!.FileMetadata.AppData.Content, Is.EqualTo(content1));
        //validate normal

        //update the content
        fileMetadata.AppData.Content = content2;

        var updateResponse = await appApiClient.V1.Drive.UpdateExistingMetadata(uploadResult.File,
            firstHeader.FileMetadata.VersionTag, fileMetadata);
        Assert.That(updateResponse.IsSuccessStatusCode, Is.True);
        var updateResult = updateResponse.Content!;

        Assert.That(updateResult.NewVersionTag, Is.Not.EqualTo(uploadResult.NewVersionTag));
        var updatedHeader = (await appApiClient.V1.Drive.GetFileHeader(uploadResult.File)).Content;

        Assert.That(updatedHeader!.FileMetadata.AppData.Content, Is.EqualTo(content2));
        Assert.That(updatedHeader.FileMetadata.VersionTag, Is.Not.EqualTo(firstHeader.FileMetadata.VersionTag));
    }

    [Test]
    public async Task CanUpdateMetadata_EvenWhenPayloadChanged_StorageIntentMedata()
    {
        var spec = SampleApp();
        var appApiClient = await SetupCaller(spec);
        var targetDrive = spec.TargetDrive;

        var content1 = OdinSystemSerializer.Serialize(new { data = "nom nom nom" });
        var content2 = OdinSystemSerializer.Serialize(new { data = "chomp chomp chomp" });

        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            AppData = new()
            {
                FileType = 101,
                Content = content1
            },
            IsEncrypted = false,
            AccessControlList = AccessControlList.Connected
        };

        //upload normal
        var uploadResponse = await appApiClient.V1.Drive.UploadNewMetadata(targetDrive, fileMetadata);
        Assert.That(uploadResponse.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadResponse.Content!;

        var firstHeader = (await appApiClient.V1.Drive.GetFileHeader(uploadResult.File)).Content;
        Assert.That(firstHeader!.FileMetadata.AppData.Content, Is.EqualTo(content1));
        //validate normal

        //update the content; indicate the payload changed
        fileMetadata.AppData.Content = content2;

        var updateResultResponse = await appApiClient.V1.Drive.UpdateExistingMetadata(uploadResult.File,
            firstHeader.FileMetadata.VersionTag, fileMetadata);

        Assert.That(updateResultResponse.IsSuccessStatusCode, Is.True);
        Assert.That(updateResultResponse.Content!.NewVersionTag, Is.Not.EqualTo(uploadResult.NewVersionTag));

        var updatedHeader = (await appApiClient.V1.Drive.GetFileHeader(uploadResult.File)).Content;

        Assert.That(updatedHeader!.FileMetadata.AppData.Content, Is.EqualTo(content2));
        Assert.That(updatedHeader.FileMetadata.VersionTag, Is.Not.EqualTo(firstHeader.FileMetadata.VersionTag));
        Assert.That(updatedHeader.FileMetadata.Payloads.Count, Is.EqualTo(0));
    }
}
