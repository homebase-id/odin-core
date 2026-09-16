using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Hosting.Tests.YouAuthApi.ApiClient.Drives;
using Odin.Services.Authentication.YouAuth;
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
/// <c>GuestSession.SetupAsync</c> is not used: it generates the circle id internally and keeps it
/// private, and this test has to name the same circle twice — once in the grant and once in the
/// uploaded file's <c>CircleIdList</c>. The domain + client registration is therefore done here
/// against a circle the test created, via the same two <c>owner.Admin</c> helpers
/// <c>GuestSession</c> itself calls. See <see cref="YouAuthDomainCaller"/>.
/// <para>
/// Request-shape notes, none of them read by an assertion: <c>owner.Admin.RegisterYouAuthDomain</c>
/// sends the identical body the original's <c>YouAuth.RegisterDomain</c> did (<c>Name =
/// "Test_{domain}"</c>, <c>ConsentRequirementType.Never</c>, zero expiration);
/// <c>owner.Admin.RegisterYouAuthClient</c> sends a different friendly name ("test in-process guest
/// client" rather than "some friendly name"); <c>owner.Admin.CreateCircle</c> supplies its own
/// description.
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
        const string domain = "amazoom.org";
        const string jsonContent = "some content";

        var owner = await LoginAsOwner();

        // Create a drive
        var targetDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive, "A secured Drive", allowAnonymousReads: false, ownerOnly: false);

        // Create a circle
        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "A circle", new PermissionSetGrantRequest()
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
        });

        var uploadResult = await UploadFile(owner, targetDrive, circleId, jsonContent);
        await AddYouAuthDomain(owner, domain, new List<GuidId>() { circleId });

        var svc = await YouAuthDomainCaller.RefitForAsync<IRefitGuestDriveQuery>(owner, new AsciiDomainName(domain));

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

    private static async Task AddYouAuthDomain(OwnerSession owner, string domainName, List<GuidId> circleIds)
    {
        var domain = new AsciiDomainName(domainName);

        var response = await owner.Admin.RegisterYouAuthDomain(domain, circleIds);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
    }
}

/// <summary>
/// A YouAuth-domain caller for a domain the test registered itself, with circles the test chose.
/// </summary>
/// <remarks>
/// <c>GuestSession</c> owns the common case — throwaway domain, circle it creates — but its circle id
/// never escapes, so a fixture that has to reference the same circle elsewhere (a file ACL, say)
/// cannot use it. This does the remaining half: register a client under the already-registered
/// domain and wrap its access token in the same in-process factory <c>GuestSession</c> builds.
/// Local to this folder; promote it if a second fixture needs the shape.
/// </remarks>
internal static class YouAuthDomainCaller
{
    public static async Task<T> RefitForAsync<T>(OwnerSession owner, AsciiDomainName domain)
    {
        var clientReg = await owner.Admin.RegisterYouAuthClient(domain);
        var cat = ClientAccessToken.FromPortableBytes(clientReg.Content!.Data);

        var factory = new InProcessApiClientFactory(
            owner.Host,
            YouAuthDefaults.XTokenCookieName,
            cat.ToAuthenticationToken(),
            cat.SharedSecret.GetKey().ToSensitiveByteArray(),
            GuestApiPathConstantsV1.BasePathV1);

        var client = factory.CreateHttpClient(owner.Identity, out var sharedSecret);
        return RefitCreator.RestServiceFor<T>(client, sharedSecret);
    }
}
