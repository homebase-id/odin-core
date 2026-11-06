#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Fluff;
using Odin.Core.Serialization;
using Odin.Core.Util;
using Odin.Hosting.Controllers.Base.Cdn;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.Cdn;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Controllers.OwnerToken.Membership.Circles;
using Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth;
using Odin.Hosting.Tests.OwnerApi.Authentication;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.UnifiedV2;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Configuration;
using Odin.Services.Configuration.Eula;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.YouAuth;
using Odin.Services.Optimization.Cdn;
using Odin.Test.Helpers;
using Refit;

namespace Odin.Hosting.Tests.V2.OwnerConsole;

/// <summary>
/// The V2 routes the owner console moved to from V1: drive, app and YouAuth domain management, configuration, circle
/// definitions, static publishing and public keys. What matters most is who is refused: an app is the owner acting
/// (<c>IsOwner</c>) with no master key, and V1 keeps it off the console's routes by keeping them on another scheme.
/// </summary>
[TestFixture]
public class V2OwnerConsoleRoutesTests : V2Fixture
{
    private const string DriveMgmtV1 = OwnerApiPathConstants.DriveManagementV1;
    private const string Circles = UnifiedApiRouteConstants.Connections + "/circles";

    /// <summary>Every route V1 keeps to the owner console: no app or guest gets in, whatever it holds.</summary>
    private static readonly (HttpMethod Method, string Path)[] OwnerOnlyRoutes =
    [
        (HttpMethod.Post, UnifiedApiRouteConstants.DriveManagement),
        (HttpMethod.Get, UnifiedApiRouteConstants.DriveManagement + "/type"),
        (HttpMethod.Post, UnifiedApiRouteConstants.DriveManagement + "/create"),
        (HttpMethod.Post, UnifiedApiRouteConstants.DriveManagement + "/update-metadata"),
        (HttpMethod.Post, UnifiedApiRouteConstants.DriveManagement + "/update-attributes"),
        (HttpMethod.Post, UnifiedApiRouteConstants.DriveManagement + "/set-drive-read-mode"),
        (HttpMethod.Post, UnifiedApiRouteConstants.DriveManagement + "/set-allow-subscriptions"),
        (HttpMethod.Post, UnifiedApiRouteConstants.DriveManagement + "/set-allow-cdn"),
        (HttpMethod.Post, UnifiedApiRouteConstants.DriveManagement + "/set-archive-drive"),
        (HttpMethod.Post, UnifiedApiRouteConstants.DriveManagement + "/empty"),
        (HttpMethod.Post, UnifiedApiRouteConstants.DriveManagement + "/delete"),
        (HttpMethod.Get, UnifiedApiRouteConstants.DriveManagement + "/purges"),
        (HttpMethod.Post, UnifiedApiRouteConstants.DriveManagement + "/purges/retry"),
        (HttpMethod.Post, UnifiedApiRouteConstants.DriveManagement + "/set-owner"),
        (HttpMethod.Post, UnifiedApiRouteConstants.DriveManagement + "/reassign-owner"),

        (HttpMethod.Get, UnifiedApiRouteConstants.AppManagement + "/list"),
        (HttpMethod.Post, UnifiedApiRouteConstants.AppManagement + "/app"),
        (HttpMethod.Post, UnifiedApiRouteConstants.AppManagement + "/register/app"),
        (HttpMethod.Post, UnifiedApiRouteConstants.AppManagement + "/register/update-app-permissions"),
        (HttpMethod.Post, UnifiedApiRouteConstants.AppManagement + "/register/update-authorized-circles"),
        (HttpMethod.Post, UnifiedApiRouteConstants.AppManagement + "/revoke"),
        (HttpMethod.Post, UnifiedApiRouteConstants.AppManagement + "/allow"),
        (HttpMethod.Post, UnifiedApiRouteConstants.AppManagement + "/uninstall"),
        (HttpMethod.Post, UnifiedApiRouteConstants.AppManagement + "/clients"),
        (HttpMethod.Post, UnifiedApiRouteConstants.AppManagement + "/revoke-client"),
        (HttpMethod.Post, UnifiedApiRouteConstants.AppManagement + "/allow-client"),
        (HttpMethod.Post, UnifiedApiRouteConstants.AppManagement + "/delete-client"),

        (HttpMethod.Post, UnifiedApiRouteConstants.Configuration + "/system/is-configured"),
        (HttpMethod.Post, UnifiedApiRouteConstants.Configuration + "/system/initialize"),
        (HttpMethod.Post, UnifiedApiRouteConstants.Configuration + "/system/is-eula-signature-required"),
        (HttpMethod.Post, UnifiedApiRouteConstants.Configuration + "/system/required-eula-version"),
        (HttpMethod.Post, UnifiedApiRouteConstants.Configuration + "/system/mark-eula-signed"),
        (HttpMethod.Post, UnifiedApiRouteConstants.Configuration + "/system/update-flag"),
        (HttpMethod.Post, UnifiedApiRouteConstants.Configuration + "/system/flags"),
        (HttpMethod.Post, UnifiedApiRouteConstants.Configuration + "/owner-app/settings/update"),
        (HttpMethod.Post, UnifiedApiRouteConstants.Configuration + "/owner-app/settings/list"),

        (HttpMethod.Get, UnifiedApiRouteConstants.YouAuthDomains + "/list"),
        (HttpMethod.Post, UnifiedApiRouteConstants.YouAuthDomains + "/domain"),
        (HttpMethod.Post, UnifiedApiRouteConstants.YouAuthDomains + "/circles/add"),
        (HttpMethod.Post, UnifiedApiRouteConstants.YouAuthDomains + "/circles/revoke"),
        (HttpMethod.Post, UnifiedApiRouteConstants.YouAuthDomains + "/revoke"),
        (HttpMethod.Post, UnifiedApiRouteConstants.YouAuthDomains + "/allow"),
        (HttpMethod.Post, UnifiedApiRouteConstants.YouAuthDomains + "/delete-domain"),
        (HttpMethod.Get, UnifiedApiRouteConstants.YouAuthDomains + "/clients?domain=example.com"),
        (HttpMethod.Post, UnifiedApiRouteConstants.YouAuthDomains + "/delete-client"),

        (HttpMethod.Post, Circles + "/definitions/set-owner"),
        (HttpMethod.Post, Circles + "/definitions/reassign-owner"),

        // Their service takes no context, so nothing behind these routes would stop an app
        (HttpMethod.Post, UnifiedApiRouteConstants.StaticContent + "/profile-card"),
        (HttpMethod.Post, UnifiedApiRouteConstants.StaticContent + "/profile-image"),
    ];

    /// <summary>On V1 these are on the owner and on apps, and the services decide; a guest has no such route.</summary>
    private static readonly (HttpMethod Method, string Path)[] OwnerOrAppRoutes =
    [
        (HttpMethod.Get, Circles + "/definitions/list"),
        (HttpMethod.Post, Circles + "/definitions/get"),
        (HttpMethod.Post, Circles + "/definitions/create"),
        (HttpMethod.Post, Circles + "/definitions/update"),
        (HttpMethod.Post, Circles + "/membership/list"),
        (HttpMethod.Post, UnifiedApiRouteConstants.StaticContent + "/publish"),
    ];

    public interface IOwnerConsoleRoutes
    {
        [Post(UnifiedApiRouteConstants.DriveManagement)]
        Task<ApiResponse<PagedResult<OwnerClientDriveData>>> GetDrives([Body] GetDrivesRequest request);

        [Post(DriveMgmtV1)]
        Task<ApiResponse<PagedResult<OwnerClientDriveData>>> GetDrivesV1([Body] GetDrivesRequest request);

        [Get(UnifiedApiRouteConstants.DriveManagement + "/type")]
        Task<ApiResponse<PagedResult<OwnerClientDriveData>>> GetDrivesByType(
            [Query] Guid driveType, [Query] int pageNumber, [Query] int pageSize);

        [Post(UnifiedApiRouteConstants.DriveManagement + "/create")]
        Task<ApiResponse<bool>> CreateDrive([Body] CreateDriveRequest request);

        [Post(UnifiedApiRouteConstants.DriveManagement + "/update-metadata")]
        Task<ApiResponse<bool>> UpdateMetadata([Body] UpdateDriveDefinitionRequest request);

        [Post(UnifiedApiRouteConstants.DriveManagement + "/update-attributes")]
        Task<ApiResponse<bool>> UpdateAttributes([Body] UpdateDriveDefinitionRequest request);

        [Post(UnifiedApiRouteConstants.DriveManagement + "/set-drive-read-mode")]
        Task<ApiResponse<HttpContent>> SetReadMode([Body] UpdateDriveReadModeRequest request);

        [Post(UnifiedApiRouteConstants.DriveManagement + "/set-allow-subscriptions")]
        Task<ApiResponse<HttpContent>> SetAllowSubscriptions([Body] UpdateDriveAllowSubscriptionsRequest request);

        [Post(UnifiedApiRouteConstants.DriveManagement + "/set-allow-cdn")]
        Task<ApiResponse<HttpContent>> SetAllowCdn([Body] UpdateDriveAllowCdnRequest request);

        [Post(UnifiedApiRouteConstants.DriveManagement + "/set-archive-drive")]
        Task<ApiResponse<HttpContent>> SetArchive([Body] UpdateDriveArchiveFlag request);

        [Post(UnifiedApiRouteConstants.DriveManagement + "/delete")]
        Task<ApiResponse<HttpContent>> DeleteDrive([Body] TargetDriveRequest request);

        [Post(UnifiedApiRouteConstants.DriveManagement + "/empty")]
        Task<ApiResponse<HttpContent>> EmptyDrive([Body] TargetDriveRequest request);

        [Get(UnifiedApiRouteConstants.DriveManagement + "/purges")]
        Task<ApiResponse<List<DrivePurgeStatus>>> GetPurges();

        [Post(UnifiedApiRouteConstants.DriveManagement + "/set-owner")]
        Task<ApiResponse<HttpContent>> SetDriveOwner([Body] SetDriveOwningAppRequest request);

        [Post(UnifiedApiRouteConstants.DriveManagement + "/reassign-owner")]
        Task<ApiResponse<HttpContent>> ReassignDriveOwner([Body] SetDriveOwningAppRequest request);

        [Get(UnifiedApiRouteConstants.AppManagement + "/list")]
        Task<ApiResponse<List<RedactedAppRegistration>>> GetApps();

        [Get(OwnerApiPathConstants.AppManagementV1 + "/list")]
        Task<ApiResponse<List<RedactedAppRegistration>>> GetAppsV1();

        [Post(UnifiedApiRouteConstants.AppManagement + "/app")]
        Task<ApiResponse<RedactedAppRegistration>> GetApp([Body] GetAppRequest request);

        [Post(UnifiedApiRouteConstants.AppManagement + "/register/app")]
        Task<ApiResponse<RedactedAppRegistration>> RegisterApp([Body] AppRegistrationRequest request);

        [Post(UnifiedApiRouteConstants.AppManagement + "/register/update-app-permissions")]
        Task<ApiResponse<HttpContent>> UpdateAppPermissions([Body] UpdateAppPermissionsRequest request);

        [Post(UnifiedApiRouteConstants.AppManagement + "/register/update-authorized-circles")]
        Task<ApiResponse<HttpContent>> UpdateAuthorizedCircles([Body] UpdateAuthorizedCirclesRequest request);

        [Post(UnifiedApiRouteConstants.AppManagement + "/revoke")]
        Task<ApiResponse<NoResultResponse>> RevokeApp([Body] GetAppRequest request);

        [Post(UnifiedApiRouteConstants.AppManagement + "/allow")]
        Task<ApiResponse<NoResultResponse>> AllowApp([Body] GetAppRequest request);

        [Post(UnifiedApiRouteConstants.AppManagement + "/uninstall")]
        Task<ApiResponse<NoResultResponse>> UninstallApp([Body] UninstallAppRequest request);

        [Post(UnifiedApiRouteConstants.AppManagement + "/clients")]
        Task<ApiResponse<List<RegisteredAppClientResponse>>> GetAppClients([Body] GetAppRequest request);

        [Post(UnifiedApiRouteConstants.AppManagement + "/revoke-client")]
        Task<ApiResponse<HttpContent>> RevokeAppClient([Body] GetAppClientRequest request);

        [Post(UnifiedApiRouteConstants.AppManagement + "/allow-client")]
        Task<ApiResponse<HttpContent>> AllowAppClient([Body] GetAppClientRequest request);

        [Post(UnifiedApiRouteConstants.AppManagement + "/delete-client")]
        Task<ApiResponse<HttpContent>> DeleteAppClient([Body] GetAppClientRequest request);

        [Post(UnifiedApiRouteConstants.Configuration + "/system/is-configured")]
        Task<ApiResponse<bool>> IsConfigured();

        [Post(UnifiedApiRouteConstants.Configuration + "/system/initialize")]
        Task<ApiResponse<bool>> Initialize([Body] InitialSetupRequest request);

        [Post(UnifiedApiRouteConstants.Configuration + "/system/is-eula-signature-required")]
        Task<ApiResponse<bool>> IsEulaRequired();

        [Post(UnifiedApiRouteConstants.Configuration + "/system/required-eula-version")]
        Task<ApiResponse<EulaVersionResponse>> RequiredEulaVersion();

        [Post(UnifiedApiRouteConstants.Configuration + "/system/mark-eula-signed")]
        Task<ApiResponse<HttpContent>> MarkEulaSigned([Body] MarkEulaSignedRequest request);

        [Post(UnifiedApiRouteConstants.Configuration + "/system/update-flag")]
        Task<ApiResponse<bool>> UpdateFlag([Body] UpdateFlagRequest request);

        [Post(UnifiedApiRouteConstants.Configuration + "/system/flags")]
        Task<ApiResponse<TenantSettings>> GetFlags();

        [Post(UnifiedApiRouteConstants.Configuration + "/owner-app/settings/update")]
        Task<ApiResponse<bool>> UpdateOwnerSettings([Body] OwnerAppSettings settings);

        [Post(UnifiedApiRouteConstants.Configuration + "/owner-app/settings/list")]
        Task<ApiResponse<OwnerAppSettings>> GetOwnerSettings();

        [Get(UnifiedApiRouteConstants.YouAuthDomains + "/list")]
        Task<ApiResponse<List<RedactedYouAuthDomainRegistration>>> GetDomains();

        [Post(UnifiedApiRouteConstants.YouAuthDomains + "/domain")]
        Task<ApiResponse<RedactedYouAuthDomainRegistration>> GetDomain([Body] GetYouAuthDomainRequest request);

        [Post(UnifiedApiRouteConstants.YouAuthDomains + "/circles/add")]
        Task<ApiResponse<bool>> GrantDomainCircle([Body] GrantYouAuthDomainCircleRequest request);

        [Post(UnifiedApiRouteConstants.YouAuthDomains + "/circles/revoke")]
        Task<ApiResponse<bool>> RevokeDomainCircle([Body] RevokeYouAuthDomainCircleRequest request);

        [Post(UnifiedApiRouteConstants.YouAuthDomains + "/revoke")]
        Task<ApiResponse<HttpContent>> RevokeDomain([Body] GetYouAuthDomainRequest request);

        [Post(UnifiedApiRouteConstants.YouAuthDomains + "/allow")]
        Task<ApiResponse<HttpContent>> AllowDomain([Body] GetYouAuthDomainRequest request);

        [Post(UnifiedApiRouteConstants.YouAuthDomains + "/delete-domain")]
        Task<ApiResponse<HttpContent>> DeleteDomain([Body] GetYouAuthDomainRequest request);

        [Get(UnifiedApiRouteConstants.YouAuthDomains + "/clients")]
        Task<ApiResponse<List<RedactedYouAuthDomainClient>>> GetDomainClients([Query] string domain);

        [Post(UnifiedApiRouteConstants.YouAuthDomains + "/delete-client")]
        Task<ApiResponse<HttpContent>> DeleteDomainClient([Body] GetYouAuthDomainClientRequest request);

        [Get(Circles + "/definitions/list")]
        Task<ApiResponse<List<CircleDefinition>>> ListCircles([Query] bool includeSystemCircle);

        [Post(Circles + "/definitions/get")]
        Task<ApiResponse<CircleDefinition>> GetCircle([Body] Guid id);

        [Post(Circles + "/definitions/create")]
        Task<ApiResponse<bool>> CreateCircle([Body] CreateCircleRequest request);

        [Post(Circles + "/definitions/update")]
        Task<ApiResponse<bool>> UpdateCircle([Body] CircleDefinition definition);

        [Post(Circles + "/definitions/set-owner")]
        Task<ApiResponse<bool>> SetCircleOwner([Body] SetCircleOwningAppRequest request);

        [Post(Circles + "/definitions/reassign-owner")]
        Task<ApiResponse<ReassignCircleOwningAppResult>> ReassignCircleOwner([Body] SetCircleOwningAppRequest request);

        [Post(Circles + "/membership/list")]
        Task<ApiResponse<List<CircleDomainResult>>> CircleMembers([Body] GetCircleMembersRequest request);

        [Post(UnifiedApiRouteConstants.StaticContent + "/publish")]
        Task<ApiResponse<StaticFilePublishResult>> Publish([Body] PublishStaticFileRequest request);

        [Post(UnifiedApiRouteConstants.StaticContent + "/profile-card")]
        Task<ApiResponse<HttpContent>> PublishProfileCard([Body] PublishPublicProfileCardRequest request);

        [Post(UnifiedApiRouteConstants.StaticContent + "/profile-image")]
        Task<ApiResponse<HttpContent>> PublishProfileImage([Body] PublishPublicProfileImageRequest request);
    }

    private static IOwnerConsoleRoutes Routes(IV2Caller caller)
    {
        var factory = caller switch
        {
            OwnerSession o => o.Factory,
            AppSession a => a.Factory,
            GuestSession g => g.Factory,
            _ => throw new ArgumentOutOfRangeException(nameof(caller))
        };

        var http = factory.CreateHttpClient(caller.Identity, out var ss);
        return RefitCreator.RestServiceFor<IOwnerConsoleRoutes>(http, ss);
    }

    private static HttpRequestMessage RequestFor(HttpMethod method, string path) =>
        new(method, path) { Content = method == HttpMethod.Post ? new StringContent("{}", Encoding.UTF8, "application/json") : null };

    private async Task<AppSession> AppHoldingEverything(OwnerSession owner)
    {
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "app drive", allowAnonymousReads: false);
        return await AppSession.SetupAsync(owner, drive, DrivePermission.All, PermissionKeys.All);
    }

    // Who is refused

    [Test]
    public async Task OwnerOnlyRoutes_RefuseAnAppHoldingEveryPermissionKey()
    {
        var owner = await LoginAsOwner();
        var app = await AppHoldingEverything(owner);
        using var http = app.Factory.CreateHttpClient(app.Identity, out _);

        foreach (var (method, path) in OwnerOnlyRoutes)
        {
            var response = await http.SendAsync(RequestFor(method, path));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"{method} {path}");
        }
    }

    [Test]
    public async Task OwnerOnlyRoutes_RefuseAnAppHoldingEverything_WhenItsTokenComesAsAHeaderAlone()
    {
        // The way a browser app calls V2 (js-lib's getHeadersV2): a bearer token, no cookie
        var owner = await LoginAsOwner();
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "app drive", allowAnonymousReads: false);
        var appSession = await AppSession.SetupAsync(owner, drive, DrivePermission.All, PermissionKeys.All);
        var (token, _) = await owner.Admin.RegisterAppClient(appSession.AppId);

        using var http = Host.CreateAnonymousClient(owner.Identity);
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.ToPortableBytes64());

        foreach (var (method, path) in OwnerOnlyRoutes)
        {
            var response = await http.SendAsync(RequestFor(method, path));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"{method} {path}");
        }
    }

    [Test]
    public async Task OwnerOnlyRoutes_RefuseAGuest_AndSoDoTheOwnerOrAppRoutes()
    {
        var owner = await LoginAsOwner();
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "guest drive", allowAnonymousReads: false);
        var guest = await GuestSession.SetupAsync(owner, drive, DrivePermission.All);
        using var http = guest.Factory.CreateHttpClient(guest.Identity, out _);

        foreach (var (method, path) in OwnerOnlyRoutes.Concat(OwnerOrAppRoutes))
        {
            var response = await http.SendAsync(RequestFor(method, path));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"{method} {path}");
        }
    }

    [Test]
    public async Task EveryRoute_RefusesAnAnonymousCaller()
    {
        using var http = Host.CreateAnonymousClient(PrimaryIdentity);

        foreach (var (method, path) in OwnerOnlyRoutes.Concat(OwnerOrAppRoutes))
        {
            var response = await http.SendAsync(RequestFor(method, path));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"{method} {path}");
        }
    }

    [Test]
    public async Task AnAppsTokenRelabeledAsOwner_IsRefused()
    {
        var owner = await LoginAsOwner();
        var app = await AppHoldingEverything(owner);
        var (appToken, _) = await owner.Admin.RegisterAppClient(app.AppId);

        var relabeled = new ClientAuthenticationToken
        {
            Id = appToken.Id,
            AccessTokenHalfKey = appToken.AccessTokenHalfKey,
            ClientTokenType = ClientTokenType.Owner
        };

        using var http = Host.CreateAnonymousClient(owner.Identity);
        http.DefaultRequestHeaders.Add("Cookie", $"{OwnerAuthConstants.CookieName}={relabeled}");

        var response = await http.SendAsync(RequestFor(HttpMethod.Post, UnifiedApiRouteConstants.Configuration + "/system/flags"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task OwnerRoutes_AreAllThere()
    {
        // A refusal proves little about a route that doesn't exist: no body goes in, so most answer 400
        var owner = await LoginAsOwner();
        using var http = owner.Factory.CreateHttpClient(owner.Identity, out _);

        foreach (var (method, path) in OwnerOnlyRoutes.Concat(OwnerOrAppRoutes))
        {
            var response = await http.SendAsync(new HttpRequestMessage(method, path));
            Assert.That(response.StatusCode, Is.Not.AnyOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed,
                HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden), $"{method} {path}");
        }
    }

    [Test]
    public async Task CircleRoutes_AdmitAnApp_WithTheChecksV1Has()
    {
        var owner = await LoginAsOwner();
        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "a circle", OwnerAdmin.ReadCircleMembershipGrant());

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "app drive", allowAnonymousReads: false);
        var noKey = Routes(await AppSession.SetupAsync(owner, drive, DrivePermission.Read));
        var withEverything = await AppHoldingEverything(owner);
        var holder = Routes(withEverything);

        // Reading needs the key
        Assert.That((await noKey.ListCircles(false)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await noKey.GetCircle(circleId)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await noKey.CircleMembers(new GetCircleMembersRequest { CircleId = circleId })).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));

        var list = await holder.ListCircles(false);
        Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(list.Content!.Any(c => c.Id == circleId), Is.True);
        Assert.That((await holder.GetCircle(circleId)).Content!.Name, Is.EqualTo("a circle"));
        Assert.That((await holder.CircleMembers(new GetCircleMembersRequest { CircleId = circleId })).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

        // Writing needs the master key, whatever the app holds
        var create = await holder.CreateCircle(new CreateCircleRequest
        {
            Id = Guid.NewGuid(), Name = "the app's own", DriveGrants = [], Permissions = new PermissionSet(PermissionKeys.ReadCircleMembership)
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        var definition = (await holder.GetCircle(circleId)).Content!;
        definition.Name = "renamed by the app";
        Assert.That((await holder.UpdateCircle(definition)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await Routes(owner).GetCircle(circleId)).Content!.Name, Is.EqualTo("a circle"));

        // And the app can't take the circle
        var move = new SetCircleOwningAppRequest { CircleId = circleId, AppId = withEverything.AppId };
        Assert.That((await holder.SetCircleOwner(move)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await holder.ReassignCircleOwner(move)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Publish_AdmitsAnAppOnlyWithItsKey()
    {
        var owner = await LoginAsOwner();
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "a drive", allowAnonymousReads: true);

        var request = new PublishStaticFileRequest
        {
            Filename = "v2-file.ok",
            Config = new StaticFileConfiguration { CrossOriginBehavior = CrossOriginBehavior.AllowAllOrigins },
            Sections =
            [
                new QueryParamSection
                {
                    Name = "s1",
                    QueryParams = new FileQueryParamsV1 { TargetDrive = drive },
                    ResultOptions = new SectionResultOptions { IncludeHeaderContent = true, PayloadKeys = [] }
                }
            ]
        };

        var noKey = await AppSession.SetupAsync(owner, drive, DrivePermission.Read);
        Assert.That((await Routes(noKey).Publish(request)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        var keyed = await AppSession.SetupAsync(owner, drive, DrivePermission.Read, [PermissionKeys.PublishStaticContent]);
        Assert.That((await Routes(keyed).Publish(request)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var asOwner = await Routes(owner).Publish(request);
        Assert.That(asOwner.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(asOwner.Content!.Filename, Is.EqualTo(request.Filename));
    }

    // The owner, end to end

    [Test]
    public async Task Drives_OwnerManagesOneFromCreateToDelete_InV1sShapes()
    {
        var owner = await LoginAsOwner();
        var routes = Routes(owner);
        var drive = TargetDrive.NewTargetDrive();
        var targetDrive = new TargetDriveRequest { TargetDrive = drive };

        var create = await routes.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = drive,
            Name = "v2 drive",
            Metadata = "",
            AllowAnonymousReads = false,
            DriveSlug = TestSlug.New(),
            DriveTypeSlug = TestSlug.DriveType
        });
        Assert.That(create.Content, Is.True, $"create: {create.StatusCode}");

        // The same answer V1 gives, field for field
        var paging = new GetDrivesRequest { PageNumber = 1, PageSize = 100 };
        var v2Drives = await routes.GetDrives(paging);
        var v1Drives = await routes.GetDrivesV1(paging);
        Assert.That(OdinSystemSerializer.Serialize(v2Drives.Content), Is.EqualTo(OdinSystemSerializer.Serialize(v1Drives.Content)));

        var byType = await routes.GetDrivesByType(drive.Type, 1, 100);
        Assert.That(byType.Content!.Results.Single().TargetDriveInfo, Is.EqualTo(drive));

        Assert.That((await routes.UpdateMetadata(new UpdateDriveDefinitionRequest { TargetDrive = drive, Metadata = "new" })).Content, Is.True);
        Assert.That((await routes.UpdateAttributes(new UpdateDriveDefinitionRequest
            { TargetDrive = drive, Attributes = new Dictionary<string, string> { { "k", "v" } } })).Content, Is.True);
        Assert.That((await routes.SetReadMode(new UpdateDriveReadModeRequest { TargetDrive = drive, AllowAnonymousReads = true })).IsSuccessStatusCode, Is.True);
        Assert.That((await routes.SetAllowSubscriptions(new UpdateDriveAllowSubscriptionsRequest { TargetDrive = drive, AllowSubscriptions = true })).IsSuccessStatusCode, Is.True);
        Assert.That((await routes.SetAllowCdn(new UpdateDriveAllowCdnRequest { TargetDrive = drive, AllowCdn = true })).IsSuccessStatusCode, Is.True);

        var changed = (await routes.GetDrivesByType(drive.Type, 1, 100)).Content!.Results.Single();
        Assert.That(changed.Metadata, Is.EqualTo("new"));
        Assert.That(changed.Attributes["k"], Is.EqualTo("v"));
        Assert.That(changed.AllowAnonymousReads, Is.True);
        Assert.That(changed.AllowSubscriptions, Is.True);
        Assert.That(changed.AllowCdn, Is.True);

        // Empty and delete refuse a drive that isn't archived, then take it, and answer 202
        Assert.That((await routes.DeleteDrive(targetDrive)).IsSuccessStatusCode, Is.False);
        Assert.That((await routes.SetArchive(new UpdateDriveArchiveFlag { TargetDrive = drive, Archived = true })).IsSuccessStatusCode, Is.True);
        Assert.That((await routes.EmptyDrive(targetDrive)).StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That((await routes.GetPurges()).IsSuccessStatusCode, Is.True);
    }

    [Test]
    public async Task Drives_OwnerHandsOneToAnApp_ThenMovesIt()
    {
        var owner = await LoginAsOwner();
        var routes = Routes(owner);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "an owner drive", allowAnonymousReads: false);

        var first = await owner.Admin.RegisterBareApp();
        var second = await owner.Admin.RegisterBareApp();

        var set = await routes.SetDriveOwner(new SetDriveOwningAppRequest
            { TargetDrive = drive, AppId = first, DriveSlug = TestSlug.New(), DriveTypeSlug = TestSlug.DriveType });
        Assert.That(set.IsSuccessStatusCode, Is.True, $"set-owner: {set.StatusCode}");

        var reassign = await routes.ReassignDriveOwner(new SetDriveOwningAppRequest
            { TargetDrive = drive, AppId = second, DriveSlug = TestSlug.New(), DriveTypeSlug = TestSlug.DriveType });
        Assert.That(reassign.IsSuccessStatusCode, Is.True, $"reassign-owner: {reassign.StatusCode}");

        Assert.That((await owner.Admin.GetDrive(drive)).AppId, Is.EqualTo(second));
    }

    [Test]
    public async Task Apps_OwnerManagesOneFromRegisterToUninstall_InV1sShapes()
    {
        var owner = await LoginAsOwner();
        var routes = Routes(owner);
        var appId = Guid.NewGuid();
        var byId = new GetAppRequest { AppId = appId };

        var register = await routes.RegisterApp(new AppRegistrationRequest
        {
            Name = "v2 app",
            AppId = appId,
            AppSlug = OwnerAdmin.SlugFor(appId),
            PermissionSet = new PermissionSet(),
            Drives = [],
            AuthorizedCircles = [],
            CircleMemberPermissionGrant = new PermissionSetGrantRequest()
        });
        Assert.That(register.IsSuccessStatusCode, Is.True, $"register: {register.StatusCode}");

        var v2Apps = await routes.GetApps();
        Assert.That(OdinSystemSerializer.Serialize(v2Apps.Content), Is.EqualTo(OdinSystemSerializer.Serialize((await routes.GetAppsV1()).Content)));
        Assert.That(v2Apps.Content!.Any(a => a.AppId == appId), Is.True);
        Assert.That((await routes.GetApp(byId)).Content!.Name, Is.EqualTo("v2 app"));

        var keys = new PermissionSet(PermissionKeys.ReadCircleMembership);
        Assert.That((await routes.UpdateAppPermissions(new UpdateAppPermissionsRequest { AppId = appId, PermissionSet = keys, Drives = [] })).IsSuccessStatusCode, Is.True);
        Assert.That((await routes.GetApp(byId)).Content!.Grant.PermissionSet.HasKey(PermissionKeys.ReadCircleMembership), Is.True);
        Assert.That((await routes.UpdateAuthorizedCircles(new UpdateAuthorizedCirclesRequest
            { AppId = appId, AuthorizedCircles = [], CircleMemberPermissionGrant = new PermissionSetGrantRequest() })).IsSuccessStatusCode, Is.True);

        var (clientToken, _) = await owner.Admin.RegisterAppClient(appId);
        var client = new GetAppClientRequest { AccessRegistrationId = clientToken.Id };
        Assert.That((await routes.GetAppClients(byId)).Content!.Any(c => c.AccessRegistrationId == clientToken.Id), Is.True);
        Assert.That((await routes.RevokeAppClient(client)).IsSuccessStatusCode, Is.True);
        Assert.That((await routes.AllowAppClient(client)).IsSuccessStatusCode, Is.True);
        Assert.That((await routes.DeleteAppClient(client)).IsSuccessStatusCode, Is.True);
        Assert.That((await routes.GetAppClients(byId)).Content, Is.Empty);

        Assert.That((await routes.RevokeApp(byId)).Content!.Success, Is.True);
        Assert.That((await routes.GetApp(byId)).Content!.IsRevoked, Is.True);
        Assert.That((await routes.AllowApp(byId)).Content!.Success, Is.True);
        Assert.That((await routes.UninstallApp(new UninstallAppRequest { AppId = appId, DeleteOwnedCirclesAndDrives = true })).Content!.Success, Is.True);
        Assert.That((await routes.GetApps()).Content!.Any(a => a.AppId == appId), Is.False);
    }

    [Test]
    public async Task Config_TheSettingsAndEulaCallsWork()
    {
        var owner = await LoginAsOwner();
        var routes = Routes(owner);

        Assert.That((await routes.IsConfigured()).IsSuccessStatusCode, Is.True);

        var flag = await routes.UpdateFlag(new UpdateFlagRequest { FlagName = nameof(TenantSettings.AnonymousVisitorsCanViewConnections), Value = "true" });
        Assert.That(flag.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(flag.Content, Is.False, "answers false, as V1 does");
        Assert.That((await routes.GetFlags()).Content!.AnonymousVisitorsCanViewConnections, Is.True);

        var settings = new Dictionary<string, string> { { "s", "\"v\"" } };
        Assert.That((await routes.UpdateOwnerSettings(new OwnerAppSettings { Settings = settings })).Content, Is.True);
        Assert.That((await routes.GetOwnerSettings()).Content!.Settings["s"], Is.EqualTo("\"v\""));

        var version = (await routes.RequiredEulaVersion()).Content!.Version;
        Assert.That((await routes.IsEulaRequired()).Content, Is.True);
        Assert.That((await routes.MarkEulaSigned(new MarkEulaSignedRequest { Version = version, SignatureBytes = [1] })).IsSuccessStatusCode, Is.True);
        Assert.That((await routes.IsEulaRequired()).Content, Is.False);
    }

    [Test]
    public async Task Config_TheSetupWizardCanInitializeTheIdentity()
    {
        var owner = await LoginAsOwner();
        var routes = Routes(owner);

        var initialize = await routes.Initialize(new InitialSetupRequest { Drives = [] });
        Assert.That(initialize.Content, Is.True, $"initialize: {initialize.StatusCode}");
        Assert.That((await routes.IsConfigured()).Content, Is.True);
    }

    [Test]
    public async Task Domains_OwnerManagesOneFromGrantToDelete_InV1sShapes()
    {
        var owner = await LoginAsOwner();
        var routes = Routes(owner);
        var domain = new AsciiDomainName("v2-domain.example.com");
        await owner.Admin.RegisterYouAuthDomain(domain);
        var byName = new GetYouAuthDomainRequest { Domain = domain.DomainName };

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "domain circle", OwnerAdmin.ReadCircleMembershipGrant());

        Assert.That((await routes.GetDomains()).Content!.Any(d => d.Domain == domain.DomainName), Is.True);
        Assert.That((await routes.GetDomain(byName)).Content!.Domain, Is.EqualTo(domain.DomainName));
        Assert.That((await routes.GetDomain(new GetYouAuthDomainRequest { Domain = "unknown.example.com" })).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));

        Assert.That((await routes.GrantDomainCircle(new GrantYouAuthDomainCircleRequest { Domain = domain.DomainName, CircleId = circleId })).Content, Is.True);
        Assert.That((await routes.CircleMembers(new GetCircleMembersRequest { CircleId = circleId })).Content!.Any(m => m.Domain.DomainName == domain.DomainName), Is.True);
        Assert.That((await routes.RevokeDomainCircle(new RevokeYouAuthDomainCircleRequest { Domain = domain.DomainName, CircleId = circleId })).Content, Is.True);

        var client = (await owner.Admin.RegisterYouAuthClient(domain)).Content!;
        Assert.That((await routes.GetDomainClients(domain.DomainName)).Content!.Any(c => c.AccessRegistrationId == client.AccessRegistrationId), Is.True);
        Assert.That((await routes.DeleteDomainClient(new GetYouAuthDomainClientRequest { AccessRegistrationId = client.AccessRegistrationId })).IsSuccessStatusCode, Is.True);

        Assert.That((await routes.RevokeDomain(byName)).IsSuccessStatusCode, Is.True);
        Assert.That((await routes.GetDomain(byName)).Content!.IsRevoked, Is.True);
        Assert.That((await routes.AllowDomain(byName)).IsSuccessStatusCode, Is.True);
        Assert.That((await routes.DeleteDomain(byName)).IsSuccessStatusCode, Is.True);
        Assert.That((await routes.GetDomain(byName)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Circles_OwnerCreatesAndChangesOne_ThenHandsItToAnApp()
    {
        var owner = await LoginAsOwner();
        var routes = Routes(owner);
        var circleId = Guid.NewGuid();

        var create = await routes.CreateCircle(new CreateCircleRequest
            { Id = circleId, Name = "v2 circle", Description = "d", DriveGrants = [], Permissions = new PermissionSet(PermissionKeys.ReadCircleMembership) });
        Assert.That(create.Content, Is.True, $"create: {create.StatusCode}");

        var definition = (await routes.GetCircle(circleId)).Content!;
        definition.Name = "renamed";
        Assert.That((await routes.UpdateCircle(definition)).Content, Is.True);
        Assert.That((await routes.ListCircles(true)).Content!.Single(c => c.Id == circleId).Name, Is.EqualTo("renamed"));
        Assert.That((await routes.CircleMembers(new GetCircleMembersRequest { CircleId = circleId })).Content, Is.Empty);

        var first = await owner.Admin.RegisterBareApp();
        var second = await owner.Admin.RegisterBareApp();
        Assert.That((await routes.SetCircleOwner(new SetCircleOwningAppRequest { CircleId = circleId, AppId = first })).Content, Is.True);

        var moved = await routes.ReassignCircleOwner(new SetCircleOwningAppRequest { CircleId = circleId, AppId = second });
        Assert.That(moved.Content!.EnrollmentsRepointed, Is.EqualTo(0));
        Assert.That((await routes.GetCircle(circleId)).Content!.AppId, Is.EqualTo(second));
    }

    [Test]
    public async Task ProfileCardAndImage_OwnerPublishes()
    {
        var routes = Routes(await LoginAsOwner());

        var card = await routes.PublishProfileCard(new PublishPublicProfileCardRequest { ProfileCardJson = "{\"name\":\"frodo\"}" });
        Assert.That(card.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var image = await routes.PublishProfileImage(new PublishPublicProfileImageRequest
            { Image64 = Convert.ToBase64String([1, 2, 3]), ContentType = "image/png" });
        Assert.That(image.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var missing = await routes.PublishProfileImage(new PublishPublicProfileImageRequest { ContentType = "image/png" });
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    // Public keys

    [Test]
    public async Task PublicKeys_AnyoneGetsThemInTheClear_LikeV1_WhateverCookieTheBrowserHolds()
    {
        var owner = await LoginAsOwner();
        const string offline = UnifiedApiRouteConstants.PublicKeys + "/offline-ecc";
        const string notifications = UnifiedApiRouteConstants.PublicKeys + "/notifications-pk";
        const string v1 = "/api/guest/v1/public/keys";

        using var anonymous = Host.CreateAnonymousClient(owner.Identity);
        var offlineKey = await anonymous.GetAsync(offline);
        Assert.That(offlineKey.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(offlineKey.Headers.CacheControl?.Public, Is.True);
        var offlineBody = await offlineKey.Content.ReadAsStringAsync();
        Assert.That(offlineBody, Is.EqualTo(await (await anonymous.GetAsync($"{v1}/offline_ecc")).Content.ReadAsStringAsync()));

        var notificationsBody = await (await anonymous.GetAsync(notifications)).Content.ReadAsStringAsync();
        Assert.That(notificationsBody, Is.EqualTo(await (await anonymous.GetAsync($"{v1}/notifications_pk")).Content.ReadAsStringAsync()));
        Assert.That(notificationsBody, Is.Not.Empty);

        // The owner console asks with a client holding no shared secret, from a browser holding the owner's cookie
        using var withOwnerCookie = Host.CreateAnonymousClient(owner.Identity);
        withOwnerCookie.DefaultRequestHeaders.Add("Cookie", $"{OwnerAuthConstants.CookieName}={owner.Token}");
        var asOwner = await withOwnerCookie.GetAsync(offline);
        Assert.That(asOwner.Headers.Contains("X-SSE"), Is.False);
        Assert.That(await asOwner.Content.ReadAsStringAsync(), Is.EqualTo(offlineBody));

        // And a session that has ended must not turn a public key into a 401
        var stale = new ClientAuthenticationToken
        {
            Id = Guid.NewGuid(),
            AccessTokenHalfKey = owner.Token.AccessTokenHalfKey,
            ClientTokenType = ClientTokenType.Owner
        };
        using var withStaleCookie = Host.CreateAnonymousClient(owner.Identity);
        withStaleCookie.DefaultRequestHeaders.Add("Cookie", $"{OwnerAuthConstants.CookieName}={stale}");
        Assert.That((await withStaleCookie.GetAsync(offline)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    // The owner's cookie and shared secret

    [Test]
    public async Task OwnerCookie_FromALogin_IsSetForEveryPath_AndAuthenticatesV2Alone()
    {
        var owner = await LoginAsOwner();

        // A login of its own, to see the Set-Cookie a browser would
        using var login = Host.CreateAnonymousClient(owner.Identity);
        var reply = await OwnerPasswordFlow.CalculateAuthenticationPasswordReplyAsync(login, OwnerLogin.DefaultPassword,
            new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1));
        var authenticated = await RestService.For<IOwnerAuthenticationClient>(login).Authenticate(reply);

        var setCookie = authenticated.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(OwnerAuthConstants.CookieName + "="));
        Assert.That(setCookie, Does.Contain("path=/;").IgnoreCase, setCookie.Split(';', 2)[1]);

        // That cookie alone: no Authorization header, which the fixture's own clients add
        using var http = Host.CreateAnonymousClient(owner.Identity);
        http.DefaultRequestHeaders.Add("Cookie", setCookie.Split(';')[0]);

        var response = await http.PostAsync(UnifiedApiRouteConstants.Configuration + "/owner-app/settings/list", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SharedSecret_IsUsedAsOnV1()
    {
        var owner = await LoginAsOwner();
        using var http = Host.CreateAnonymousClient(owner.Identity);
        http.DefaultRequestHeaders.Add("Cookie", $"{OwnerAuthConstants.CookieName}={owner.Token}");

        // The answer comes back encrypted, and the shared secret opens it
        var response = await http.PostAsync(UnifiedApiRouteConstants.Configuration + "/system/flags", null);
        Assert.That(response.Headers.GetValues("X-SSE").Single(), Is.EqualTo("1"));
        var flags = SharedSecretEncryptedPayload.Decrypt(await response.Content.ReadAsByteArrayAsync(), owner.SharedSecret);
        Assert.That(flags.ToStringFromUtf8Bytes(), Does.Contain("anonymousVisitorsCanViewConnections"));

        // A body in the clear is refused, on V2 as on V1
        var clear = new StringContent("{\"pageNumber\":1,\"pageSize\":10}", Encoding.UTF8, "application/json");
        var v2 = await http.PostAsync(UnifiedApiRouteConstants.DriveManagement, clear);
        var v1 = await http.PostAsync(DriveMgmtV1, clear);
        Assert.That(v2.IsSuccessStatusCode, Is.False);
        Assert.That(v2.StatusCode, Is.EqualTo(v1.StatusCode));

        // So is a query in the clear
        var query = await http.GetAsync($"{UnifiedApiRouteConstants.DriveManagement}/type?driveType={Guid.NewGuid()}");
        Assert.That(query.IsSuccessStatusCode, Is.False);
    }
}
