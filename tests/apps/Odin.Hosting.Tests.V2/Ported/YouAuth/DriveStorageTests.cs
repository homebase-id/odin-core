using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.YouAuthApi.ApiClient.Drives;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.YouAuth;

/// <summary>
/// Port of <c>YouAuthApi/Drive/DriveStorageTests</c>. An anonymous (no-token) caller is refused the
/// header and the payload of a file whose ACL requires a connection, even on an
/// anonymous-readable drive.
/// </summary>
/// <remarks>
/// Caller is <c>Host.AnonymousRefitFor<T>()</c> — the no-credential client, standing in for the original's
/// <c>WebScaffold.CreateAnonymousApiHttpClient</c>. See that class for why a <c>GuestSession</c>
/// would be a different caller.
/// <para>
/// The original pinned <c>TestIdentities.Samwise</c> and reached it via
/// <c>TestIdentities.InitializedIdentities</c>, which is null under <c>V2Fixture</c>. Nothing here
/// reads the identity, so the fixture default serves and the owner session is passed down.
/// </para>
/// <para>
/// The original also carried a second, entirely unused private helper (<c>UploadFilexx</c>, which
/// went through <c>OldOwnerApi.Upload</c> with a payload). Dead code, dropped rather than moved.
/// The surviving <c>UploadFile</c> never writes a payload, so
/// <see cref="ShouldFailToGetSecuredFile_Payload"/> asks for a payload key that was never uploaded —
/// carried as found, since the refusal it asserts happens at the drive check, before anything looks
/// for the payload.
/// </para>
/// <para>
/// No <c>SetupCallerWithOwner</c> (no caller matrix), so its ordering caveat does not apply.
/// </para>
/// </remarks>
[TestFixture]
public class DriveStorageTests : V2Fixture
{
    [Test]
    public async Task ShouldFailToGetSecuredFile_Header()
    {
        var owner = await LoginAsOwner();
        var tag = Guid.NewGuid();
        var uploadContext = await UploadFile(owner, tag, SecurityGroupType.Connected);

        var svc = Host.AnonymousRefitFor<IRefitGuestDriveQuery>(owner.Identity);
        var getHeaderResponse = await svc.GetFileHeader(
            new ExternalFileIdentifier()
            {
                TargetDrive = uploadContext.UploadResult.File.TargetDrive,
                FileId = uploadContext.UploadResult.File.FileId
            });

        Assert.That(getHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task ShouldFailToGetSecuredFile_Payload()
    {
        var owner = await LoginAsOwner();
        var tag = Guid.NewGuid();
        var uploadContext = await UploadFile(owner, tag, SecurityGroupType.Connected);

        var svc = Host.AnonymousRefitFor<IRefitGuestDriveQuery>(owner.Identity);

        var getPayloadStreamResponse = await svc.GetPayload(
            new GetPayloadRequest()
            {
                File = new ExternalFileIdentifier()
                {
                    TargetDrive = uploadContext.UploadResult.File.TargetDrive,
                    FileId = uploadContext.UploadResult.File.FileId
                },
                Key = WebScaffold.PAYLOAD_KEY
            });

        Assert.That(getPayloadStreamResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(getPayloadStreamResponse.Content, Is.Null);
    }

    private static async Task<(UploadResult UploadResult, UploadFileMetadata UploadedFileMetadata)> UploadFile(
        OwnerSession owner, Guid tag, SecurityGroupType requiredSecurityGroup)
    {
        List<Guid> tags = new List<Guid>() { tag };

        var uploadFileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = false,
            IsEncrypted = false,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                FileType = 100,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                Tags = tags
            },
            AccessControlList = new AccessControlList()
            {
                RequiredSecurityGroup = requiredSecurityGroup
            }
        };

        var td = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(td, "a drive", allowAnonymousReads: true);
        var response = await owner.V1.Drive.UploadNewMetadata(td, uploadFileMetadata);
        return (response.Content, uploadFileMetadata);
    }
}
