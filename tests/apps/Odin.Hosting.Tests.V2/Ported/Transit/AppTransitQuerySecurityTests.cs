using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests.AppAPI.ApiClient.Transit.Query;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/AppAPI/Transit/Query/AppTransitQuerySecurityTests.cs
///
/// An app may only reach another identity over transit query when it holds
/// <see cref="PermissionKeys.UseTransitRead"/>: every transit-query endpoint answers 403 without it.
/// The first test is the positive half — a connected identity's app sees read / react / comment on
/// each of the remote's anonymous drives.
/// </summary>
/// <remarks>
/// The original's class was named <c>AppTransitQueryPermissionTests</c> while its file was named for
/// security; the class is renamed to match the file, and nothing else about it moves.
/// <para>
/// Port notes:
/// <list type="bullet">
///   <item><description>
///     The four copies of the original's private <c>CreateAppAndClient</c> are
///     <see cref="AppTransitClients.CreateAppAsync"/>, and <c>AppApiClient.TransitQuery</c> is the
///     <c>IRefitAppTransitQuery</c> surface reached as the app — see that class for why.
///   </description></item>
///   <item><description>
///     The drive listing goes through <c>owner.Admin.GetDrives</c>, which pages at 1000 rather than
///     the original's 200 and throws instead of handing back a response to assert on. Neither
///     changes what the test sees: Pippin holds a couple of dozen drives at most.
///   </description></item>
///   <item><description>
///     The permission checks in tests 2-7 are enforced on the calling identity before any peer call,
///     so the identity named in each request body is never resolved — the original said so in a
///     comment, and it is why <c>GetModified</c> and the four after it name Merry as the remote.
///   </description></item>
///   <item><description>
///     The original's <c>Task.Delay(5)</c> before <c>GetModified</c> is dropped: it was there for
///     the modified-file cursor in the sibling fixtures, and this test prepares no data at all.
///   </description></item>
///   <item><description>
///     Both apps are registered once in <see cref="WarmTenantBaselineAsync"/> and baked into the
///     baseline snapshot rather than re-registered per test: app registrations and their client
///     tokens live in the identity DB the per-test reset restores. Nothing else in the arrange is
///     shared — test 1 makes its own connection, and the refusal tests prepare no data at all.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
[TestFixture]
public class AppTransitQuerySecurityTests : V2Fixture
{

    private AppSession _merryAppWithTransitRead;
    private AppSession _merryAppWithoutTransitRead;

    protected override string[] HostIdentities => [Identities.Merry, Identities.Pippin];

    /// <summary>
    /// The two apps the fixture reads through: the one test 1 needs, holding
    /// <see cref="PermissionKeys.UseTransitRead"/>, and the one the six refusal tests share, holding
    /// everything except it.
    /// </summary>
    protected override async Task WarmTenantBaselineAsync()
    {
        await base.WarmTenantBaselineAsync();

        var merry = await LoginAsOwner(Identities.Merry);
        _merryAppWithTransitRead = await AppTransitClients.CreateAppAsync(merry, PermissionKeys.UseTransitRead);
        _merryAppWithoutTransitRead = await AppTransitClients.CreateAppAsync(merry,
            PermissionKeys.UseTransitWrite, PermissionKeys.ReadConnections);
    }

    [Test]
    public async Task SystemDefault_AppHas_Read_React_Comment_Permissions_On_AnonymousDrives_WhenConnected()
    {
        var merry = await LoginAsOwner(Identities.Merry);
        var pippin = await LoginAsOwner(Identities.Pippin);

        var sendRequest = await pippin.Connections.SendConnectionRequest(merry.Identity);
        Assert.That(sendRequest.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var accept = await merry.Connections.AcceptConnectionRequest(pippin.Identity);
        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var allPippinDrives = await pippin.Admin.GetDrives();
        var expectedAnonymousDrives = allPippinDrives.Where(drive => drive.AllowAnonymousReads).ToList();

        var remoteDotYouContextResponse = await _merryAppWithTransitRead.RefitFor<IRefitAppTransitQuery>()
            .GetRemoteDotYouContext(new TransitGetSecurityContextRequest { OdinId = pippin.Identity });

        Assert.That(remoteDotYouContextResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(remoteDotYouContextResponse.Content, Is.Not.Null);
        var groups = remoteDotYouContextResponse.Content!.PermissionContext.PermissionGroups;

        var drivesWithoutTheExpectedGrant = expectedAnonymousDrives.Where(ownerAnonDrive =>
            !groups.Any(pg => pg.DriveGrants.Any(dg =>
                dg.PermissionedDrive.Drive == ownerAnonDrive.TargetDriveInfo &&
                dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Read) &&
                dg.PermissionedDrive.Permission.HasFlag(DrivePermission.React) &&
                dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Comment))))
            .Select(d => d.TargetDriveInfo);

        Assert.That(drivesWithoutTheExpectedGrant, Is.Empty);
    }

    [Test]
    public async Task AppFailsTo_GetBatch_OverTransitQuery_Without_UseTransitRead_Permission()
    {
        //Note: I do not prepare any remote data because the permission is enforced on the origin identity
        var svc = _merryAppWithoutTransitRead.RefitFor<IRefitAppTransitQuery>();
        var getBatchResponse = await svc.GetBatch(new PeerQueryBatchRequest
        {
            OdinId = Identities.Pippin
        });

        Assert.That(getBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppFailsTo_GetModified_OverTransitQuery_Without_UseTransitRead_Permission()
    {
        //Note: I do not prepare any remote data because the permission is enforced on the origin identity
        var svc = _merryAppWithoutTransitRead.RefitFor<IRefitAppTransitQuery>();
        var getBatchResponse = await svc.GetModified(new PeerQueryModifiedRequest
        {
            OdinId = Identities.Merry
        });

        Assert.That(getBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppFailsTo_GetHeader_OverTransitQuery_Without_UseTransitRead_Permission()
    {
        //Note: I do not prepare any remote data because the permission is enforced on the origin identity
        var svc = _merryAppWithoutTransitRead.RefitFor<IRefitAppTransitQuery>();
        var getBatchResponse = await svc.GetFileHeader(new TransitExternalFileIdentifier
        {
            OdinId = Identities.Merry,
            File = new ExternalFileIdentifier
            {
                FileId = Guid.NewGuid(),
                TargetDrive = TargetDrive.NewTargetDrive()
            }
        });

        Assert.That(getBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppFailsTo_GetPayload_OverTransitQuery_Without_UseTransitRead_Permission()
    {
        //Note: I do not prepare any remote data because the permission is enforced on the origin identity
        var svc = _merryAppWithoutTransitRead.RefitFor<IRefitAppTransitQuery>();
        var getBatchResponse = await svc.GetPayload(new TransitGetPayloadRequest
        {
            OdinId = Identities.Merry,
            File = new ExternalFileIdentifier
            {
                FileId = Guid.NewGuid(),
                TargetDrive = TargetDrive.NewTargetDrive()
            },
            Key = WebScaffold.PAYLOAD_KEY
        });

        Assert.That(getBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppFailsTo_GetThumbnails_OverTransitQuery_Without_UseTransitRead_Permission()
    {
        //Note: I do not prepare any remote data because the permission is enforced on the origin identity
        var svc = _merryAppWithoutTransitRead.RefitFor<IRefitAppTransitQuery>();
        var getBatchResponse = await svc.GetThumbnail(new TransitGetThumbRequest
        {
            OdinId = Identities.Merry,
            File = new ExternalFileIdentifier
            {
                FileId = Guid.NewGuid(),
                TargetDrive = TargetDrive.NewTargetDrive()
            }
        });

        Assert.That(getBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AppFailsTo_GetBatchCollection_OverTransitQuery_Without_UseTransitRead_Permission()
    {
        //Note: I do not prepare any remote data because the permission is enforced on the origin identity
        var svc = _merryAppWithoutTransitRead.RefitFor<IRefitAppTransitQuery>();
        var getBatchResponse = await svc.GetBatchCollection(new PeerQueryBatchCollectionRequest
        {
            OdinId = Identities.Merry,
            Queries = new List<CollectionQueryParamSection>
            {
                new()
                {
                    Name = "test01",
                    QueryParams = default,
                    ResultOptionsRequest = default
                }
            }
        });

        Assert.That(getBatchResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
