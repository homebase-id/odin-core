using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.YouAuthApi.ApiClient.Drives;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.YouAuth;

/// <summary>
/// Port of <c>YouAuthApi/Auth/BasicAuthenticationTests</c>. A registered YouAuth domain reads a file
/// on a secured drive because the circle it was granted holds Read on that drive and is named in the
/// file's ACL.
/// </summary>
/// <remarks>
/// <c>GuestSession.SetupAsync</c> does the whole of the setup: it creates the circle from the grant
/// it is handed, registers the domain against it, and registers a client under the domain. This test
/// has to name that circle a second time, in the uploaded file's <c>CircleIdList</c>, which is what
/// <c>GuestSession.CircleId</c> is for.
/// <para>
/// Request-shape notes, none of them read by an assertion: <c>GuestSession</c> registers the domain
/// with the identical body the original's <c>YouAuth.RegisterDomain</c> sent (<c>Name =
/// "Test_{domain}"</c>, <c>ConsentRequirementType.Never</c>, zero expiration) and registers the
/// client with a different friendly name ("test in-process guest client" rather than "some friendly
/// name"); its <c>CreateCircle</c> supplies its own description.
/// </para>
/// <para>
/// Ordering note: <c>GuestSession.SetupAsync</c> registers the domain <i>before</i> the file is
/// uploaded, where the original registered it after. Harmless — the ACL is evaluated when the guest
/// reads, not when the domain is registered — and confirmed by running.
/// </para>
/// <para>
/// The original pinned <c>TestIdentities.Merry</c>; nothing here reads the identity, so the fixture
/// default serves. No <c>SetupCallerWithOwner</c>, so its ordering caveat does not apply.
/// </para>
/// </remarks>
[TestFixture]
public class BasicAuthenticationTests : V2Fixture
{
    [Test]
    public async Task YouAuthDomainCanAccessAuthorizedContentViaCircle()
    {
        const string jsonContent = "some content";

        var owner = await LoginAsOwner();

        // Create a drive
        var targetDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive, "A secured Drive", allowAnonymousReads: false, ownerOnly: false);

        // A guest domain granted Read on that drive, via a circle whose id the file's ACL names too.
        var guest = await GuestSession.SetupAsync(owner, new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(),
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Read
                    }
                }
            }
        }, new AsciiDomainName("amazoom.org"));

        var uploadResult = await UploadFile(owner, targetDrive, guest.CircleId, jsonContent);

        var svc = guest.RefitFor<IRefitGuestDriveQuery>();

        var getFileHeaderResponse = await svc.GetFileHeader(uploadResult.File);
        Assert.That(getFileHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var fileHeader = getFileHeaderResponse.Content;
        Assert.That(fileHeader.FileMetadata.AppData.Content, Is.EqualTo(jsonContent));
    }

    private static async Task<UploadResult> UploadFile(OwnerSession owner, TargetDrive targetDrive, Guid circleId,
        string jsonContent)
    {
        var standardFile = new UploadFileMetadata()
        {
            IsEncrypted = false,
            AllowDistribution = true,
            AppData = new()
            {
                Content = jsonContent,
                FileType = 101,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                Tags = default
            },
            AccessControlList = new AccessControlList()
            {
                RequiredSecurityGroup = SecurityGroupType.Authenticated,
                CircleIdList = new List<Guid>() { circleId }
            }
        };

        var response = await owner.V1.Drive.UploadNewMetadata(targetDrive, standardFile, FileSystemType.Standard);
        return response.Content;
    }

    [Test, Explicit("TODO")]
    public void ConnectedIdentityCanAccessAuthorizedContent()
    {
        Assert.Inconclusive("todo");
    }
}
