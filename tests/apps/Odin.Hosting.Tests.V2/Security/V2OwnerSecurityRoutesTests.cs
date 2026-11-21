#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Login;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.System.Table;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.ZeroKnowledge;
using Odin.Hosting.UnifiedV2;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Hosting.UnifiedV2.Mail;
using Odin.Hosting.UnifiedV2.Security;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Drives;
using Odin.Services.Security;
using Odin.Services.Security.ZeroKnowledge;
using Refit;

namespace Odin.Hosting.Tests.V2.Security;

/// <summary>
/// The owner console's security routes on V2: who is refused, what travels encrypted, and that the recovery routes
/// answer whoever calls, as on V1. The routes are read off the controllers, so one added later can't be left out.
/// </summary>
[TestFixture]
public class V2OwnerSecurityRoutesTests : V2Fixture
{
    private const string Password = OwnerLogin.DefaultPassword;

    protected override string[] HostIdentities => [Identities.Frodo];

    public interface IRoutes
    {
        [Get(UnifiedApiRouteConstants.SecurityZk)]
        Task<ApiResponse<ZkStatusResponse>> GetZkStatus();

        [Get(UnifiedApiRouteConstants.SecurityZk + "/session-key")]
        Task<ApiResponse<ZkSessionKeyResponse>> GetSessionKey();

        [Post(UnifiedApiRouteConstants.SecurityZk + "/phrase-handover")]
        Task<ApiResponse<ZkPhraseHandoverResponse>> HandOverPhrase([Body] ZkPhraseHandoverRequest request);

        [Post(UnifiedApiRouteConstants.Security + "/reset-password")]
        Task<ApiResponse<HttpContent>> ResetPassword([Body] ResetPasswordRequest request);

        [Get(UnifiedApiRouteConstants.DataConversion + "/data-version-info")]
        Task<ApiResponse<VersionInfoResult>> GetVersionInfo();
    }

    private static readonly Type[] OwnerControllers =
    [
        typeof(V2ZeroKnowledgeController),
        typeof(V2OwnerSecurityController),
        typeof(V2SecurityConfigurationController),
        typeof(V2SecurityVerificationController),
        typeof(V2DnsHealthController),
        typeof(V2OwnerMailController),
        typeof(V2DataConversionController)
    ];

    private static IEnumerable<(HttpMethod Method, string Path)> RoutesOf(Type controller)
    {
        var root = controller.GetCustomAttribute<RouteAttribute>()!.Template;
        foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            foreach (var verb in action.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>())
            {
                var path = string.IsNullOrEmpty(verb.Template) ? root : $"{root}/{verb.Template}";
                yield return (new HttpMethod(verb.HttpMethods.Single()), path.Replace("{driveId:guid}", Guid.NewGuid().ToString()));
            }
        }
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string path) =>
        new(method, path)
        {
            Content = method == HttpMethod.Post ? new StringContent("{}", Encoding.UTF8, "application/json") : null
        };

    private static async Task AssertEveryOwnerRouteRefusesAsync(HttpClient client, string who)
    {
        foreach (var (method, path) in OwnerControllers.SelectMany(RoutesOf))
        {
            var response = await client.SendAsync(NewRequest(method, path));
            Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden),
                $"{who} must be refused on {method} {path}");
        }
    }

    private async Task<(ClientAuthenticationToken Token, byte[] SharedSecret, AppSession App)> NewAppAsync(OwnerSession owner)
    {
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "app drive", allowAnonymousReads: false);
        var appId = Guid.NewGuid();
        await owner.Admin.RegisterApp(appId, new PermissionSetGrantRequest());
        var (token, secret) = await owner.Admin.RegisterAppClient(appId);
        return (token, secret, AppSession.FromToken(owner, appId, token, secret));
    }

    [Test]
    public void EveryOwnerControllerIsOwnerOnly_AndTheShamirRoutesRunNoScheme()
    {
        foreach (var controller in OwnerControllers)
        {
            Assert.That(controller.GetCustomAttribute<UnifiedV2AuthorizeAttribute>()?.Policy, Is.EqualTo(UnifiedPolicies.Owner),
                controller.Name);
        }

        Assert.That(typeof(V2ShamirRecoveryController).GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>(), Is.Null,
            "any scheme here would make a browser holding a cookie encrypt, or a stale cookie a 401");
    }

    [Test]
    public async Task AnAppIsRefusedOnEveryOwnerRoute()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var (_, _, app) = await NewAppAsync(owner);

        using var client = app.Factory.CreatePlainHttpClient(owner.Identity);
        await AssertEveryOwnerRouteRefusesAsync(client, "an app");
    }

    [Test]
    public async Task AGuestIsRefusedOnEveryOwnerRoute()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "guest drive", allowAnonymousReads: false);
        var guest = await GuestSession.SetupAsync(owner, drive, Odin.Services.Drives.DrivePermission.Read);

        using var client = guest.Factory.CreatePlainHttpClient(owner.Identity);
        await AssertEveryOwnerRouteRefusesAsync(client, "a guest");
    }

    [Test]
    public async Task NoLoginIsRefusedOnEveryOwnerRoute()
    {
        using var client = Host.CreateAnonymousClient(Identities.Frodo);
        await AssertEveryOwnerRouteRefusesAsync(client, "no login");
    }

    /// <summary>
    /// The token's type is its last byte, which the client sets, and the claim the policy reads comes from it. An app that
    /// flips it to Owner must get nothing more.
    /// </summary>
    [Test]
    [Explicit("Fails today, on V1 too: OdinContextCache keys a context by id and half key, not the token's type, so an app's cached " +
              "context answers an owner token made from its token. Passes with UpdateOdinContextAsync refusing a context with no " +
              "master key; that fix is waiting on Todd's decision.")]
    public async Task AnAppTokenRetypedAsOwnerIsRefused()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var (token, secret, app) = await NewAppAsync(owner);

        // Puts the app's context in the cache, as any request of the app does
        using (var appClient = app.Factory.CreateHttpClient(owner.Identity, out _))
        {
            var verified = await appClient.GetAsync($"{UnifiedApiRouteConstants.Auth}/verify-token");
            Assert.That(verified.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        var retyped = new ClientAuthenticationToken
        {
            Id = token.Id,
            AccessTokenHalfKey = token.AccessTokenHalfKey,
            ClientTokenType = ClientTokenType.Owner
        };

        using var bearer = Host.CreateAnonymousClient(Identities.Frodo);
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", retyped.ToPortableBytes64());
        await AssertEveryOwnerRouteRefusesAsync(bearer, "an app with its token retyped, V2");

        foreach (var inCookie in new[] { retyped, token })
        {
            using var cookie = Host.CreateAnonymousClient(Identities.Frodo);
            cookie.DefaultRequestHeaders.Add("Cookie", $"{OwnerAuthConstants.CookieName}={inCookie}");
            var v1 = await cookie.GetAsync($"{OwnerApiPathConstants.MailV1}/status");
            Assert.That(v1.StatusCode, Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden), "V1, an app's token in the owner cookie");
        }
    }

    /// <summary>The cookie alone, with no Authorization header, as a browser sends it; and it is set for every path.</summary>
    [Test]
    public async Task TheOwnerCookieReachesV2()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        using var client = Host.CreateAnonymousClient(Identities.Frodo);
        client.DefaultRequestHeaders.Add("Cookie", $"{OwnerAuthConstants.CookieName}={owner.Token}");
        var response = await client.GetAsync(UnifiedApiRouteConstants.SecurityZk);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.Contains("X-SSE"), Is.True);

        var login = await OwnerPasswordFlow.LoginAsync(Host, Identities.Frodo, Password,
            new Odin.Core.Cryptography.Data.EccFullKeyData(
                Odin.Core.Cryptography.Crypto.EccKeyListManagement.zeroSensitiveKey,
                Odin.Core.Cryptography.Data.EccKeySize.P384, 1));
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(OwnerAuthConstants.CookieName + "="));
        Assert.That(cookie, Does.Contain("path=/;").Or.EndWith("path=/"));
        Assert.That(cookie.ToLowerInvariant(), Does.Contain("httponly").And.Contain("samesite=strict").And.Contain("secure"));
    }

    /// <summary>What a key travels in: the same encrypted envelope as V1, opening to the same answer.</summary>
    [Test]
    public async Task KeyResponsesAreEncrypted_LikeV1()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        await ZkOwnerSteps.CreateKeysAsync(owner, Password);
        var (client, secret) = owner.NewAdminHttpClient();

        foreach (var (v1, v2, secretField) in new[]
                 {
                     ($"{OwnerApiPathConstants.SecurityV1}/zk/session-key", $"{UnifiedApiRouteConstants.SecurityZk}/session-key", "sessionKey64"),
                     ($"{OwnerApiPathConstants.SecurityV1}/zk", UnifiedApiRouteConstants.SecurityZk, "zkSaltKek64")
                 })
        {
            var onV1 = await client.GetAsync(v1);
            var onV2 = await client.GetAsync(v2);
            Assert.That(onV1.StatusCode, Is.EqualTo(HttpStatusCode.OK), v1);
            Assert.That(onV2.StatusCode, Is.EqualTo(HttpStatusCode.OK), v2);
            Assert.That(onV2.Headers.Contains("X-SSE"), Is.True, v2);

            var raw = await onV2.Content.ReadAsStringAsync();
            Assert.That(raw, Does.Not.Contain(secretField), "the field must not be readable on the wire");

            var v1Json = SharedSecretEncryptedPayload.Decrypt(await onV1.Content.ReadAsStringAsync(), secret).ToStringFromUtf8Bytes();
            var v2Json = SharedSecretEncryptedPayload.Decrypt(raw, secret).ToStringFromUtf8Bytes();
            Assert.That(v2Json, Does.Contain(secretField));
            Assert.That(v2Json, Is.EqualTo(v1Json), "the same shape and values as V1");
        }
    }

    [Test]
    public async Task ACleartextBodyIsRefusedOnAnEncryptedRoute()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        using var client = owner.Factory.CreatePlainHttpClient(owner.Identity);

        var response = await client.PostAsync($"{UnifiedApiRouteConstants.SecurityZk}/phrase-handover",
            new StringContent("{\"currentAuthenticationPasswordReply\":null}", Encoding.UTF8, "application/json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("phrase"));
    }

    /// <summary>The routes V1 exempts by path are exempt on V2 by attribute: nothing else travels in the clear.</summary>
    [Test]
    public void OnlyTheRoutesV1ExemptsAreInTheClear()
    {
        string[] exempt =
        [
            "VerifyPassword", "GetRecoveryInfo", "UpdateMonthlyReportStatus", "GetSecurityHealthReportStatus",
            "UpdateRecoveryEmail", "RecoveryNeedsAttention"
        ];

        foreach (var controller in OwnerControllers)
        {
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var isExempt = exempt.Contains(action.Name) && controller == typeof(V2SecurityVerificationController);
                Assert.That(action.GetCustomAttribute<NoSharedSecretOnRequestAttribute>() != null, Is.EqualTo(isExempt), $"{action.Name}, request");
                Assert.That(action.GetCustomAttribute<NoSharedSecretOnResponseAttribute>() != null, Is.EqualTo(isExempt), $"{action.Name}, response");
            }
        }
    }

    [Test]
    public async Task TheExemptRoutesAnswerInTheClear_LikeV1()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        using var client = owner.Factory.CreatePlainHttpClient(owner.Identity);

        foreach (var route in new[] { "needs-attention", "monthly-security-health-report-status", "recovery-info?live=false" })
        {
            var onV1 = await client.GetAsync($"{OwnerApiPathConstants.SecurityRecoveryV1}/{route}");
            var onV2 = await client.GetAsync($"{UnifiedApiRouteConstants.SecurityRecovery}/{route}");

            Assert.That(onV2.StatusCode, Is.EqualTo(HttpStatusCode.OK), route);
            Assert.That(onV2.Headers.Contains("X-SSE"), Is.False, route);
            Assert.That(await onV2.Content.ReadAsStringAsync(), Is.EqualTo(await onV1.Content.ReadAsStringAsync()), route);
        }

        var updated = await client.PostAsync(
            $"{UnifiedApiRouteConstants.SecurityRecovery}/update-monthly-security-health-report-status?enabled=true", null);
        Assert.That(updated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task VerifyPassword_TakesItsProofInTheClear_AndRefusesAWrongOne()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        using var client = owner.Factory.CreatePlainHttpClient(owner.Identity);

        async Task<HttpResponseMessage> VerifyAsync(string password)
        {
            var reply = await OwnerPasswordFlow.CalculateAuthenticationPasswordReplyAsync(Host, Identities.Frodo, password);
            return await client.PostAsync($"{UnifiedApiRouteConstants.SecurityRecovery}/verify-password",
                new StringContent(OdinSystemSerializer.Serialize(reply), Encoding.UTF8, "application/json"));
        }

        Assert.That((await VerifyAsync(Password)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await VerifyAsync("not the password")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task PhraseHandover_NeedsProofOfThePassword_AndTheAnswerOpens()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var routes = owner.RefitFor<IRoutes>();

        var wrong = await routes.HandOverPhrase(new ZkPhraseHandoverRequest
        {
            CurrentAuthenticationPasswordReply =
                await OwnerPasswordFlow.CalculateAuthenticationPasswordReplyAsync(Host, Identities.Frodo, "not the password")
        });
        Assert.That(wrong.IsSuccessStatusCode, Is.False);

        var right = await routes.HandOverPhrase(new ZkPhraseHandoverRequest
        {
            CurrentAuthenticationPasswordReply =
                await OwnerPasswordFlow.CalculateAuthenticationPasswordReplyAsync(Host, Identities.Frodo, Password)
        });
        Assert.That(right.StatusCode, Is.EqualTo(HttpStatusCode.OK), right.Error?.Content);
        Assert.That(right.Content!.Phrase.Split(' '), Has.Length.EqualTo(12));
    }

    [Test]
    public async Task ThePasswordChangesOnV2()
    {
        const string newPassword = "a new password for the test";
        var owner = await LoginAsOwner(Identities.Frodo);

        var request = await OwnerPasswordFlow.BuildResetPasswordRequestAsync(Host, Identities.Frodo, Password, newPassword);
        var changed = await owner.RefitFor<IRoutes>().ResetPassword(request);
        Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.OK), changed.Error?.Content);

        var again = await OwnerSession.LoginAsync(Host, Identities.Frodo, newPassword);
        Assert.That((await again.RefitFor<IRoutes>().GetZkStatus()).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>
    /// The recovery routes answer as V1 does, in the clear, whatever the browser holds: nothing, a live owner cookie, a
    /// stale one, or an app's token.
    /// </summary>
    [Test]
    public async Task TheRecoveryRoutesAnswerAsV1_WhateverTheCallerHolds()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var (appToken, _, _) = await NewAppAsync(owner);
        var stale = new ClientAuthenticationToken
        {
            Id = Guid.NewGuid(),
            AccessTokenHalfKey = Guid.NewGuid().ToByteArray().ToSensitiveByteArray(),
            ClientTokenType = ClientTokenType.Owner
        };

        var held = new (string Who, Action<HttpClient> Set)[]
        {
            ("nothing", _ => { }),
            ("a live owner cookie", c => c.DefaultRequestHeaders.Add("Cookie", $"{OwnerAuthConstants.CookieName}={owner.Token}")),
            ("a stale owner cookie", c => c.DefaultRequestHeaders.Add("Cookie", $"{OwnerAuthConstants.CookieName}={stale}")),
            ("an app's token", c => c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appToken.ToPortableBytes64()))
        };

        var requests = new (HttpMethod Method, string Route, string? Body)[]
        {
            (HttpMethod.Get, "status", null),
            (HttpMethod.Get, "recovery-bundle", null),
            (HttpMethod.Post, "initiate-recovery-mode", "{\"recoveryPublicKeyJwk\":\"not a key\"}"),
            (HttpMethod.Post, "verify-enter", $"{{\"id\":\"{Guid.NewGuid()}\",\"recoveryPublicKeyJwk\":\"not a key\"}}"),
            (HttpMethod.Post, "fill-slot", "{\"shardId\":\"cut off by a mail client\",\"code\":\"x\"}"),
            (HttpMethod.Post, "finalize", "{\"secret64\":\"AAAA\",\"passwordReply\":null}"),
            (HttpMethod.Post, "exit-recovery-mode", null)
        };

        foreach (var (who, set) in held)
        {
            using var client = Host.CreateAnonymousClient(Identities.Frodo);
            set(client);

            foreach (var (method, route, body) in requests)
            {
                HttpRequestMessage Message(string root) => new(method, $"{root}/{route}")
                {
                    Content = body == null ? null : new StringContent(body, Encoding.UTF8, "application/json")
                };

                var onV1 = await client.SendAsync(Message(OwnerApiPathConstants.SecurityRecoveryV1));
                var onV2 = await client.SendAsync(Message(UnifiedApiRouteConstants.SecurityRecovery));

                var what = $"{method} {route} holding {who}";
                Assert.That(onV2.StatusCode, Is.EqualTo(onV1.StatusCode), what);
                Assert.That(onV2.Headers.Contains("X-SSE"), Is.False, what);
                Assert.That(onV2.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized), what);
            }
        }

        // And reachable with no login at all, with its real answer
        using var anonymous = Host.CreateAnonymousClient(Identities.Frodo);
        var status = await anonymous.GetAsync($"{UnifiedApiRouteConstants.SecurityRecovery}/status");
        Assert.That(status.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(JsonDocument.Parse(await status.Content.ReadAsStringAsync()).RootElement.GetProperty("state").GetString(), Is.EqualTo("none"));
    }

    [Test]
    public async Task AWrappedShardLinkIsRefusedAsOnV1()
    {
        using var client = Host.CreateAnonymousClient(Identities.Frodo);
        var response = await client.PostAsync($"{UnifiedApiRouteConstants.SecurityRecovery}/fill-slot",
            new StringContent("{\"shardId\":\"cut\",\"code\":\"x\"}", Encoding.UTF8, "application/json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(),
            Does.Contain(OdinClientErrorCode.ShardLinkNoLongerValid.ToString()).IgnoreCase);
    }

    /// <summary>An owner's request on V2 schedules the data upgrade, as one on V1 does.</summary>
    [Test]
    public async Task AnOwnerRequestOnV2SchedulesTheUpgrade()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var jobs = scope.Resolve<TableJobs>();
        var before = (await jobs.GetAllJobsAsync()).Count(IsUpgradeJob);

        await scope.Resolve<TenantConfigService>().ForceVersionNumberAsync(Odin.Services.Version.DataVersionNumber - 1);

        var info = await owner.RefitFor<IRoutes>().GetVersionInfo();
        Assert.That(info.StatusCode, Is.EqualTo(HttpStatusCode.OK), info.Error?.Content);
        Assert.That(info.Content!.RequiresUpgrade, Is.True, "the arrangement: the identity is behind");
        Assert.That((await jobs.GetAllJobsAsync()).Count(IsUpgradeJob), Is.GreaterThan(before));

        static bool IsUpgradeJob(JobsRecord job) => job.jobType == VersionUpgradeJob.JobTypeId.ToString();
    }
}

/// <summary>Set apart because the run state is a tenant singleton, as in <c>VersionUpgradeGuardTests</c>.</summary>
[TestFixture]
[NonParallelizable]
public class V2OwnerSecurityDuringUpgradeTests : V2Fixture
{
    [Test]
    public async Task VersionInfoAnswersOnV2DuringAnUpgrade_WhileTheRestIsRefused()
    {
        var owner = await LoginAsOwner(Identities.TomBombadil);
        var runState = Host.GetTenantScope(owner.Identity.DomainName).Resolve<VersionUpgradeRunState>();
        var (client, _) = owner.NewAdminHttpClient();

        runState.SetRunning(true);
        try
        {
            var ordinary = await client.GetAsync(UnifiedApiRouteConstants.SecurityZk);
            Assert.That(ordinary.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));

            var versionInfo = await client.GetAsync($"{UnifiedApiRouteConstants.DataConversion}/data-version-info");
            Assert.That(versionInfo.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(versionInfo.Headers.Contains(OdinHeaderNames.UpgradeIsRunning), Is.True);
        }
        finally
        {
            runState.SetRunning(false);
        }
    }
}
