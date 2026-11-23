#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Ported.Peer;
using Odin.Services.Peer.AppNotification;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Refit;

namespace Odin.Hosting.Tests.V2.Security;

/// <summary>
/// An app refused for a permission key its token lacks gets the error code <c>missingPermissionKey</c> and the keys any one
/// of which would have done (<c>permissionKeys</c>), so its client can tell that from the other identity refusing it,
/// which also answers 403.
/// </summary>
[TestFixture]
public class MissingPermissionKeyTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Pippin];

    [Test]
    public async Task APeerUpdateWithoutUseTransitWriteNamesIt()
    {
        var app = await AppWithAsync(PermissionKeys.UseTransitRead);
        var drive = TargetDrive.NewTargetDrive();
        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected, allowDistribution: true);

        var refused = await V2PeerUpdateByGtidTests.UpdateAsync(app, (Odin.Core.Identity.OdinId)Identities.Frodo, drive, Guid.NewGuid(),
            metadata, V2PeerUpdateByGtidTests.Instructions(drive, new UploadManifest { PayloadDescriptors = [] }));

        AssertMissing(refused, PermissionKeys.UseTransitWrite);
    }

    [Test]
    public async Task APeerNotificationTokenWithoutUseTransitReadNamesIt()
    {
        var app = await AppWithAsync(PermissionKeys.UseTransitWrite);

        var refused = await app.RefitFor<IDrivePeerNotificationHttpClientApiV2>()
            .GetToken(new GetRemoteTokenRequest { Identity = (Odin.Core.Identity.OdinId)Identities.Frodo });

        AssertMissing(refused, PermissionKeys.UseTransitRead);
    }

    /// <summary>An app of Pippin's holding only <paramref name="key"/></summary>
    private async Task<AppSession> AppWithAsync(int key)
    {
        var pippin = await LoginAsOwner(Identities.Pippin);
        var drive = TargetDrive.NewTargetDrive();
        await pippin.Admin.CreateDrive(drive, "app drive");
        return await AppSession.SetupAsync(pippin, drive, DrivePermission.ReadWrite, [key]);
    }

    private static void AssertMissing(IApiResponse response, int key)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), response.Error?.Content);
        var problem = OdinSystemSerializer.Deserialize<Microsoft.AspNetCore.Mvc.ProblemDetails>(response.Error!.Content!)!;
        Assert.That(Enum.Parse<OdinClientErrorCode>(problem.Extensions["errorCode"]!.ToString()!, true),
            Is.EqualTo(OdinClientErrorCode.MissingPermissionKey), response.Error.Content);
        var keys = ((JsonElement)problem.Extensions["permissionKeys"]!).EnumerateArray().Select(k => k.GetInt32());
        Assert.That(keys, Is.EqualTo(new[] { key }), response.Error.Content);
    }
}
