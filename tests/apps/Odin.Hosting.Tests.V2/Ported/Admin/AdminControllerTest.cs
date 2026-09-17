#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Admin.Tenants;
using Odin.Services.Admin.Tenants.Jobs;
using Odin.Services.Authorization.Acl;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.JobManagement;

namespace Odin.Hosting.Tests.V2.Ported.Admin;

/// <summary>
/// Port of <c>tests/apps/Odin.Hosting.Tests/AdminApi/AdminControllerTest.cs</c>. The fleet-admin
/// surface on <c>/api/admin/v1</c>: list tenants, read one by domain (with and without payload
/// figures), the per-tenant metrics report and its promise to supersede the by-domain endpoint,
/// export a tenant, and the enable/disable toggles for a tenant and for its public web presence.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item><b>The gate is config, so it moves to <see cref="V2Fixture.ConfigOverrides"/>.</b> The
/// original set <c>Admin__*</c> through <c>RunBeforeAnyTests(envOverrides:)</c>; env vars are
/// process-wide, <c>ConfigOverrides</c> is per host. The sibling fixture
/// <c>AdminApi/AdminApiRestrictedAttributeTest</c> — which asserts what happens with the API off, on
/// the wrong port and on the wrong domain — is NOT ported here: two of its three negative cases are
/// about a Kestrel listener that TestServer never binds.</item>
///
/// <item><b>The admin API is reached on a hand-built client, not <c>owner.Admin</c>.</b> It is a
/// separate pipeline branch (<c>Startup</c> maps it by <c>Request.Host.Host == Admin:Domain</c>,
/// outside <c>UseMultiTenancy</c>) authenticated by an API key header, so no owner session addresses
/// it and the "Admin for arrange, RefitFor for the SUT" rule does not apply: every call here is the
/// SUT and every call is raw HTTP, exactly as in the original. <c>owner.Admin</c> appears only in
/// <see cref="CreatePayload"/>, as arrange.</item>
///
/// <item><b><c>AdminApiRestrictedAttribute</c> compares <c>Connection.LocalPort</c> against
/// <c>Admin:ApiPort</c>, and TestServer sets no connection feature at all</b> — <c>LocalPort</c> is
/// 0, while the attribute rejects an <c>ApiPort</c> below 1 at construction, so there is no
/// configuration that makes the two agree. <see cref="CreateAdminClient"/> therefore stamps the port
/// onto the context through <c>TestServer.CreateHandler(Action&lt;HttpContext&gt;)</c>. That is the
/// in-process stand-in for the second Kestrel listener the original talked to, and it is the only
/// part of the admin gate this framework cannot exercise for real.
/// <para>
/// Note the shape is "force the check to pass", not "make <c>LocalPort</c> true", which is why the
/// gate itself stays untested. The unblocking change, if a second admin fixture ever arrives, is to
/// derive the port from the request instead — <c>context.Connection.LocalPort = context.Request.Host.Port ?? 443</c>,
/// in <see cref="OdinHost"/> rather than here. It is cheap and safe: <c>Connection.LocalPort</c> is
/// read in exactly one place in the whole product (<c>AdminApiRestrictedAttribute</c>), and these
/// URLs already carry <c>:4444</c>, so no test body would change. The payoff is that
/// <c>AdminApiRestrictedAttributeTest.PingShouldReturn404IfWrongPort</c> becomes portable — point a
/// client at <c>:443</c> and get the 404 — leaving only its genuinely connection-refused case on
/// WebScaffold. Deliberately not done here: one fixture does not meet the promotion bar.
/// </para></item>
///
/// <item><b>The two job-polling loops become one explicit run.</b> The original slept 100 ms up to
/// 20 times waiting for <c>JobRunnerBackgroundService</c> to pick the export job up. Background
/// services are registered but never started here, so that loop would burn its 20 attempts and fail;
/// <c>IJobManager.RunJobNowAsync</c> (documented on the interface as the way to drive a job from a
/// test) replaces it. Everything the loop asserted afterwards — the job reached
/// <c>JobState.Succeeded</c>, its target path, the delete/delete-again/404 sequence, the two exported
/// directories — is carried unchanged.</item>
///
/// <item><b><c>ItShouldGetAllTenants</c> asserts more than one tenant, and the fixture still boots
/// only Frodo.</b> The extra registrations are not an accident of either framework: the effective
/// <c>Development:PreconfiguredDomains</c> is the six-domain list in
/// <c>appsettings.development.json</c>, and neither the V1 env var (which cannot express a JSON
/// array) nor this framework's per-host in-memory override (which replaces index 0 only) shortens
/// it. Listing a second identity in <c>HostIdentities</c> would buy nothing and cost a tenant
/// materialisation plus a reset per test.</item>
///
/// <item><b>The <c>RUN_S3_TESTS</c> branches are dropped, and that loses coverage — read this
/// before re-adding them.</b> The V1 original asserted the payload path against
/// <c>Path.Combine(S3Storage__ServiceUrl, S3Payload__BucketName, tenant.Id)</c> under
/// <c>#if RUN_S3_TESTS</c>, and skipped the export test under <c>#if !RUN_S3_TESTS</c>. That works
/// on <c>WebScaffold</c>, which configures S3 when the constant is defined. It cannot work here:
/// <see cref="OdinHost"/> hard-sets <c>S3PayloadStorage__Enabled=false</c> and stores payloads
/// locally whatever the constant says. Carried verbatim, the S3 branch compiled on CI (which does
/// define <c>RUN_S3_TESTS</c>) and compared a local path against a bare tenant id — two failures
/// that no Debug run reproduces, because Debug takes the <c>#else</c>. So both fixtures now always
/// assert the local path, and the export test always runs.
/// <para>
/// The consequence, stated plainly: <b>the S3 payload-path assertion does not survive the move.</b>
/// It was real coverage on <c>WebScaffold</c> and there is no equivalent here until the fast host
/// can be pointed at S3. Tracked in #1782 rather than left as a comment.
/// </para></item>
///
/// <item><b>Carried as-is:</b> the <c>#if false</c> delete-tenant test with its "disabled until we
/// figure out a way to do this without racing the rest of the system" note. That block does not
/// compile in either framework, so its V2 spelling is untested; it is kept because deleting it would
/// erase the only record that tenant deletion is deliberately uncovered.</item>
///
/// <item><b>Not carried:</b> the original's <c>[SetUp]</c> assertions that
/// <c>Host__TenantDataRootPath</c> was non-empty and existed. They checked the V1 env-var plumbing
/// that no longer exists — the path now comes from this host's own <see cref="OdinConfiguration"/> —
/// and they were setup sanity, not coverage. Also not carried: <c>[CancelAfter(60000)]</c>, which
/// guarded the two polling loops that are now single calls, and which no fixture in this framework
/// uses.</item>
///
/// <item><b>Arrange substitution:</b> <see cref="CreatePayload"/> uploads through
/// <see cref="AppFileUploads.UploadEncryptedAsync"/> rather than the V1 <c>DriveApiClient.UploadFile</c>.
/// The two differ in one respect that reaches the server: the V1 helper sends the payload in the
/// clear with <c>IsEncrypted = false</c>, this one encrypts it, so the stored payload is a few bytes
/// longer. Checked: no assertion here reads a payload length, only "greater than zero" and the
/// equality of two endpoints' views of the same number.</item>
///
/// <item><b>Latent defect in the original, carried:</b> <c>UploadStandardFileToChannel</c> took an
/// <c>uploadedContent</c> argument ("I'm Mr. Underhill") and never used it — the file's
/// <c>AppData.Content</c> was left empty. Nothing asserts on content, so the behaviour is unchanged
/// here and the argument is simply gone.</item>
///
/// <item><b>Two assertions in the original that cannot fail, carried verbatim:</b>
/// <c>TenantModel.Id</c> is a <c>string</c> that defaults to <c>""</c>, so
/// <c>Assert.That(tenant.Id, Is.Not.Null)</c> and <c>Is.Not.EqualTo(Guid.Empty)</c> are both
/// vacuously true — the second compares a boxed string against a boxed <c>Guid</c> and never reaches
/// an equality operator, the same trap the README records for <c>GuidId</c> vs <c>Guid</c>. Tightening
/// them would be a coverage change, so they stay as they are; the id is checked for real one test
/// over, in <see cref="ItShouldGetTenantMetrics"/>, which parses it.</item>
///
/// <item>The log-event invariant is on and needs no tolerations: nothing this fixture does — including
/// reading metrics for the five registered-but-never-materialised tenants, which opens a scope per
/// tenant on SQLite — logs at Error.</item>
///
/// <item>The <c>Odin-Admin-Api-Key</c> header the original also sent on its <i>tenant</i>-facing
/// requests (verifyToken) is dropped: nothing on that pipeline reads it.</item>
///
/// <item>No caller matrix in the original — the admin API has one caller, the API key — and none
/// added.</item>
///
/// <item><see cref="V2Fixture.SetupCallerWithOwner"/> is not used; the ordering caveat about it does
/// not apply.</item>
///
/// <item><b>State that outlives a reset.</b> The enable/disable toggles write to the identity
/// registry, not to the identity DB, so per-test reset does not undo them; both tests re-enable what
/// they turned off, exactly as the original did. The export job lives in the system database, which
/// reset also leaves alone, and the export test deletes it as part of its own assertions.</item>
/// </list>
/// </remarks>
[TestFixture]
public class AdminControllerTest : V2Fixture
{
    private const string AdminDomain = "admin.dotyou.cloud";
    private const int AdminPort = 4444;
    private const string AdminApiKey = "your-secret-api-key-here";
    private const string AdminApiKeyHeaderName = "Odin-Admin-Api-Key";

    private readonly string _exportTargetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

    // null! rather than nullable: both are assigned in WarmTenantBaselineAsync, which the fixture
    // runs before any test. The file is #nullable enable, so without this the CI Release build --
    // which uses --warnaserror -- fails on CS8618 before a test runs.
    private OwnerSession _frodo = null!;
    private TargetDrive _payloadDrive = null!;

    protected override IReadOnlyDictionary<string, string?> ConfigOverrides =>
        new Dictionary<string, string?>
        {
            ["Admin:ApiEnabled"] = "true",
            ["Admin:ApiKey"] = AdminApiKey,
            ["Admin:ApiKeyHttpHeaderName"] = AdminApiKeyHeaderName,
            ["Admin:ApiPort"] = AdminPort.ToString(),
            ["Admin:Domain"] = AdminDomain,
            ["Admin:ExportTargetPath"] = _exportTargetPath,
        };

    /// <summary>
    /// The export target sits outside the host's data root, so <see cref="OdinHost"/> does not clean
    /// it up. This is the original's <c>[TearDown]</c> delete, moved to the end of the fixture.
    /// </summary>
    /// <summary>
    /// The enable/disable flags live in the identity registry, which the per-test reset does not
    /// restore (see <c>OdinHost.ResetAsync</c>). Both toggle tests re-enable what they turned off as
    /// their last step, exactly as the original did — but if one fails midway, the tenant stays
    /// disabled and every later test in the fixture fails for an unrelated reason. This is idempotent
    /// lifecycle restoration, which <see cref="V2Fixture"/> owns; the asserted toggles in the test
    /// bodies are untouched.
    /// </summary>
    [TearDown]
    public async Task RestoreTenantFlags()
    {
        using var apiClient = CreateAdminClient();
        await SendAsync(apiClient, HttpMethod.Patch, $"tenants/{Identities.Frodo}/enable");
        await SendAsync(apiClient, HttpMethod.Patch, $"tenants/{Identities.Frodo}/public-web-presence/enable");
    }

    [OneTimeTearDown]
    public void DeleteExportTarget()
    {
        if (Directory.Exists(_exportTargetPath))
        {
            Directory.Delete(_exportTargetPath, true);
        }
    }

    /// <summary>
    /// Baked into the baseline: Frodo's owner session and the channel drive the payload tests upload
    /// to. Both survive the per-test DB restore, so the five tests that seed a payload no longer pay
    /// a login (three client-side PBKDF2 passes at 100k iterations, plus a fourth server-side) and a
    /// drive create each. The upload itself stays in <see cref="CreatePayload"/> — the payload tree
    /// is wiped between tests, unlike the drive record. Same shape as
    /// <c>Ported/Transit/AppTransitQueryTestsForPublicFiles</c>.
    /// </summary>
    protected override async Task WarmTenantBaselineAsync()
    {
        await base.WarmTenantBaselineAsync();

        _frodo = await LoginAsOwner(Identities.Frodo);

        _payloadDrive = new TargetDrive { Alias = Guid.NewGuid(), Type = SystemDriveConstants.ChannelDriveType };
        await _frodo.Admin.CreateDrive(_payloadDrive, "A Channel Drive", allowAnonymousReads: false,
            ownerOnly: false);
    }

    private OdinConfiguration Config => Host.Server.Services.GetRequiredService<OdinConfiguration>();

    private string TenantDataRootPath => Config.Host.TenantDataRootPath;

    /// <summary>
    /// A client onto the admin branch of the pipeline. <c>BaseAddress</c> names the admin domain so
    /// <c>Startup</c>'s <c>MapWhen</c> takes it, and the handler stamps
    /// <see cref="ConnectionInfo.LocalPort"/> because <c>AdminApiRestrictedAttribute</c> checks it
    /// and TestServer leaves it at 0 — see the fixture remarks.
    /// </summary>
    private HttpClient CreateAdminClient() =>
        new(Host.Server.CreateHandler(context => context.Connection.LocalPort = AdminPort))
        {
            BaseAddress = new Uri($"https://{AdminDomain}:{AdminPort}/")
        };

    /// <summary>
    /// Relative on purpose: <see cref="CreateAdminClient"/> already carries the admin origin as its
    /// <c>BaseAddress</c>, and spelling the absolute prefix here too meant a reader had to work out
    /// which of the two was load-bearing for <c>MapWhen</c>'s host match. It is the request URI, so
    /// the origin now appears in exactly one place.
    /// </summary>
    private static string AdminUrl(string relativePath) => $"api/admin/v1/{relativePath}";

    private static HttpRequestMessage NewRequestMessage(HttpMethod method, string uri)
    {
        return new HttpRequestMessage(method, uri)
        {
            Headers = { { AdminApiKeyHeaderName, AdminApiKey } }
        };
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string relativePath) =>
        client.SendAsync(NewRequestMessage(method, AdminUrl(relativePath)));

    /// <summary>
    /// The request/assert-OK/deserialize triplet that ran to three lines at roughly a dozen call
    /// sites. Folding it keeps the route under test visible at the call site and puts the status
    /// assertion somewhere it cannot be forgotten.
    /// </summary>
    private static async Task<T> GetOkAsync<T>(HttpClient client, string relativePath)
    {
        var response = await SendAsync(client, HttpMethod.Get, relativePath);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return await ReadAsync<T>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        OdinSystemSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync())!;

    //

    [Test]
    public async Task ItShouldGetAllTenants()
    {
        using var apiClient = CreateAdminClient();
        var tenants = await GetOkAsync<List<TenantModel>>(apiClient, "tenants");
        Assert.That(tenants.Count, Is.GreaterThan(1));
        Assert.That(tenants, Has.Some.Matches<TenantModel>(t => t.Domain == Identities.Frodo));
    }

    //

    [Test]
    public async Task ItShouldGetTenantMetrics()
    {
        using var apiClient = CreateAdminClient();
        var metrics = await GetOkAsync<TenantMetricsResponse>(apiClient, "tenants/metrics");

        Assert.That(metrics.GeneratedAt.milliseconds, Is.GreaterThan(0));
        Assert.That(metrics.DatabaseType, Is.EqualTo(Config.Database.Type.ToString().ToLowerInvariant()));
        // The cross-tenant scan needs one shared database; SQLite gives each tenant its own file.
        Assert.That(metrics.IndexOrphanScanSupported,
            Is.EqualTo(Config.Database.Type == DatabaseType.Postgres));

        var frodo = metrics.Tenants.SingleOrDefault(t => t.Domain == Identities.Frodo);
        Assert.That(frodo, Is.Not.Null);
        Assert.That(frodo!.Registered, Is.True);
        Assert.That(frodo.OrphanSource, Is.Null);
        Assert.That(frodo.Enabled, Is.True);
        Assert.That(frodo.DriveCount, Is.Not.Null.And.GreaterThan(0));
        Assert.That(frodo.Files, Is.Not.Null);
        Assert.That(frodo.ActiveBytes, Is.Not.Null.And.LessThanOrEqualTo(frodo.TotalBytes!));

        // The id must be a canonical UUID string, and the same one the tenant list reports.
        Assert.That(Guid.TryParse(frodo.Id, out _), Is.True, $"not a canonical UUID: {frodo.Id}");

        // Unasserted status, as in the original: the deserialize below is what would fail.
        var tenant = await ReadAsync<TenantModel>(
            await SendAsync(apiClient, HttpMethod.Get, $"tenants/{Identities.Frodo}"));
        Assert.That(frodo.Id, Is.EqualTo(tenant.Id));

        // Every row carries a canonical id, registered or not - that is the whole point for a
        // consumer that wants to join an orphan back to a log line or a support ticket.
        Assert.That(metrics.Tenants, Has.All.Matches<TenantMetricsModel>(t => Guid.TryParse(t.Id, out _)));
        Assert.That(metrics.Tenants, Has.All.Matches<TenantMetricsModel>(t => t.Registered == (t.Domain != null)));
    }

    //

    /// <summary>
    /// Read cold, after the upload. These figures come through the table caches, so a read taken
    /// before the upload would still be served from cache afterwards -- see the note on
    /// TenantAdmin.MetricsCacheTtl.
    /// </summary>
    [Test]
    public async Task ItShouldReportBytesAfterUpload()
    {
        await CreatePayload();

        using var apiClient = CreateAdminClient();

        var metrics = await GetOkAsync<TenantMetricsResponse>(apiClient, "tenants/metrics");
        var frodo = metrics.Tenants.Single(t => t.Domain == Identities.Frodo);

        Assert.That(frodo.Files, Is.Not.Null.And.GreaterThan(0));
        Assert.That(frodo.TotalBytes, Is.Not.Null.And.GreaterThan(0));
        Assert.That(frodo.ActiveBytes, Is.Not.Null.And.GreaterThan(0));
        Assert.That(frodo.DriveCount, Is.Not.Null.And.GreaterThan(0));

        // Nothing was deleted, so every byte is live.
        Assert.That(frodo.ActiveBytes, Is.EqualTo(frodo.TotalBytes));

        // Same figure the existing endpoint reports: both sum byteCount over every file state.
        var tenant = await ReadAsync<TenantModel>(
            await SendAsync(apiClient, HttpMethod.Get, $"tenants/{Identities.Frodo}?include-payload=true"));
        Assert.That(frodo.TotalBytes, Is.EqualTo(tenant.PayloadSize));
    }

    //

    /// <summary>
    /// The metrics endpoint must be a superset of the tenant endpoint, so a caller collecting
    /// storage figures never has to make a second call to fill in the gaps.
    /// </summary>
    [Test]
    public async Task ItShouldSupersedeTheTenantEndpoint()
    {
        await CreatePayload();

        using var apiClient = CreateAdminClient();

        var metrics = await ReadAsync<TenantMetricsResponse>(
            await SendAsync(apiClient, HttpMethod.Get, "tenants/metrics"));
        var frodoMetrics = metrics.Tenants.Single(t => t.Domain == Identities.Frodo);

        var tenant = await ReadAsync<TenantModel>(
            await SendAsync(apiClient, HttpMethod.Get, $"tenants/{Identities.Frodo}?include-payload=true"));

        // Every field the old endpoint carries has an equivalent here.
        Assert.That(frodoMetrics.Id, Is.EqualTo(tenant.Id));
        Assert.That(frodoMetrics.Domain, Is.EqualTo(tenant.Domain));
        Assert.That(frodoMetrics.Enabled, Is.EqualTo(tenant.Enabled));
        Assert.That(frodoMetrics.EnablePublicWebPresence, Is.EqualTo(tenant.EnablePublicWebPresence));
        Assert.That(frodoMetrics.RegistrationPath, Is.EqualTo(tenant.RegistrationPath));
        Assert.That(frodoMetrics.RegistrationSize, Is.EqualTo(tenant.RegistrationSize));
        Assert.That(frodoMetrics.PayloadPath, Is.EqualTo(tenant.PayloadPath));
        Assert.That(frodoMetrics.TotalBytes, Is.EqualTo(tenant.PayloadSize));
    }

    //

    /// <summary>
    /// "tenants/metrics" is a literal segment and must win over the "tenants/{domain}" parameter.
    /// </summary>
    [Test]
    public async Task ItShouldNotShadowTheTenantByDomainRoute()
    {
        using var apiClient = CreateAdminClient();

        var metrics = await GetOkAsync<TenantMetricsResponse>(apiClient, "tenants/metrics");
        Assert.That(metrics.Tenants, Is.Not.Empty);

        var tenant = await GetOkAsync<TenantModel>(apiClient, $"tenants/{Identities.Frodo}");
        Assert.That(tenant.Domain, Is.EqualTo(Identities.Frodo));
    }

    //

    [Test]
    public async Task ItShouldGetSpecificTenant()
    {
        using var apiClient = CreateAdminClient();
        var tenant = await GetOkAsync<TenantModel>(apiClient, $"tenants/{Identities.Frodo}");
        Assert.That(tenant.Domain, Is.EqualTo(Identities.Frodo));
        Assert.That(tenant.RegistrationPath, Does.StartWith(TenantDataRootPath));
        Assert.That(tenant.RegistrationPath, Does.EndWith(tenant.Id));
        Assert.That(tenant.RegistrationPath, Does.Exist);

        if (Config.Database.Type == DatabaseType.Sqlite)
        {
            Assert.That(tenant.RegistrationSize, Is.GreaterThan(0));
        }

        Assert.That(tenant.PayloadPath, Is.Null);
        Assert.That(tenant.PayloadSize, Is.Null);
    }

    //

    [Test]
    public async Task ItShouldGetSpecificTenantWithNonExistingPayloads()
    {
        using var apiClient = CreateAdminClient();
        var tenant = await GetOkAsync<TenantModel>(apiClient, $"tenants/{Identities.Frodo}?include-payload=true");
        Assert.That(tenant.Id, Is.Not.Null);
        Assert.That(tenant.Id, Is.Not.EqualTo(Guid.Empty));

        Assert.That(tenant.Domain, Is.EqualTo(Identities.Frodo));
        Assert.That(tenant.RegistrationPath, Does.StartWith(TenantDataRootPath));
        Assert.That(tenant.RegistrationPath, Does.EndWith(tenant.Id));
        Assert.That(tenant.RegistrationPath, Does.Exist);

        if (Config.Database.Type == DatabaseType.Sqlite)
        {
            Assert.That(tenant.RegistrationSize, Is.GreaterThan(0));
        }

        // No #if RUN_S3_TESTS branch here, unlike the V1 original -- see the class remarks. This
        // host always stores payloads locally, so the S3 path assertion could never hold.
        var pm = new TenantPathManager(Config, Guid.Parse(tenant.Id));
        Assert.That(tenant.PayloadPath, Is.EqualTo(pm.PayloadsPath));

        Assert.That(tenant.PayloadSize, Is.Not.Null.And.EqualTo(0));
    }

    //

    [Test]
    public async Task ItShouldGetSpecificTenantWithExistingPayload()
    {
        await CreatePayload();

        using var apiClient = CreateAdminClient();
        var tenant = await GetOkAsync<TenantModel>(apiClient, $"tenants/{Identities.Frodo}?include-payload=true");

        Assert.That(tenant.Id, Is.Not.Null);
        Assert.That(tenant.Id, Is.Not.EqualTo(Guid.Empty));

        Assert.That(tenant.Domain, Is.EqualTo(Identities.Frodo));
        Assert.That(tenant.RegistrationPath, Does.StartWith(TenantDataRootPath));
        Assert.That(tenant.RegistrationPath, Does.EndWith(tenant.Id));

        if (Config.Database.Type == DatabaseType.Sqlite)
        {
            Assert.That(tenant.RegistrationSize, Is.GreaterThan(0));
        }

        // No #if RUN_S3_TESTS branch here, unlike the V1 original -- see the class remarks.
        var pm = new TenantPathManager(Config, Guid.Parse(tenant.Id));
        Assert.That(tenant.PayloadPath, Is.EqualTo(pm.PayloadsPath));
        Assert.That(tenant.PayloadPath, Does.StartWith(TenantDataRootPath));

        Assert.That(tenant.PayloadSize, Is.Not.Null.And.GreaterThan(0));
    }

    //

// SEB:TODO disabled until we figure out a way to do this without racing the rest of the system
#if false
    [Test]
    public async Task ItShouldDeleteTenant()
    {
        await CreatePayload();

        var url = AdminUrl($"tenants/{Identities.Frodo}");

        using var apiClient = CreateAdminClient();
        var request = NewRequestMessage(HttpMethod.Delete, url);
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        // Asserting the header itself rather than a TryGetValues bool: a missing or reshaped
        // Location now prints the value (or null) instead of "Expected: True".
        Assert.That(response.Headers.Location?.ToString(),
            Does.StartWith($"https://{AdminDomain}:{AdminPort}/api/job/v1/"));
        var location = response.Headers.Location!.ToString();

        var jobManager = Host.Server.Services.GetRequiredService<IJobManager>();
        var jobId = JobIdFrom(location);
        await jobManager.RunJobNowAsync(jobId, CancellationToken.None);

        request = NewRequestMessage(HttpMethod.Get, location);
        response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var jobResponse = JobApiResponse.Deserialize(await response.Content.ReadAsStringAsync());
        Assert.That(jobResponse.State, Is.EqualTo(JobState.Succeeded));
        Assert.That(jobResponse.JobId, Is.Not.Null);

        Assert.That(await jobManager.JobExistsAsync(jobId), Is.True);
        Assert.That(await jobManager.DeleteJobByIdAsync(jobId), Is.True);
        Assert.That(await jobManager.JobExistsAsync(jobId), Is.False);
        Assert.That(await jobManager.DeleteJobByIdAsync(jobId), Is.False);

        request = NewRequestMessage(HttpMethod.Get, location);
        response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
#endif

    //

    // SEB:TODO update for S3 payloads
    [Test]
    public async Task ItShouldExportTenant()
    {
        await CreatePayload();

        var url = AdminUrl($"tenants/{Identities.Frodo}/export");
        using var apiClient = CreateAdminClient();
        var request = NewRequestMessage(HttpMethod.Post, url);
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        // Asserting the header itself rather than a TryGetValues bool: a missing or reshaped
        // Location now prints the value (or null) instead of "Expected: True".
        Assert.That(response.Headers.Location?.ToString(),
            Does.StartWith($"https://{AdminDomain}:{AdminPort}/api/job/v1/"));
        var location = response.Headers.Location!.ToString();

        var jobManager = Host.Server.Services.GetRequiredService<IJobManager>();
        var jobId = JobIdFrom(location);

        // Stands in for the original's poll loop: nothing starts the job runner in this framework.
        await jobManager.RunJobNowAsync(jobId, CancellationToken.None);

        request = NewRequestMessage(HttpMethod.Get, location);
        response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var (jobResponse, exportData) =
            JobApiResponse.Deserialize<ExportTenantJobData>(await response.Content.ReadAsStringAsync());
        Assert.That(jobResponse.State, Is.EqualTo(JobState.Succeeded));

        Assert.That(jobResponse.JobId, Is.Not.Null);
        Assert.That(exportData?.TargetPath, Is.EqualTo(Path.Combine(_exportTargetPath, Identities.Frodo)));

        Assert.That(await jobManager.JobExistsAsync(jobId), Is.True);
        Assert.That(await jobManager.DeleteJobByIdAsync(jobId), Is.True);
        Assert.That(await jobManager.JobExistsAsync(jobId), Is.False);
        Assert.That(await jobManager.DeleteJobByIdAsync(jobId), Is.False);

        request = NewRequestMessage(HttpMethod.Get, location);
        response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        Assert.That(Path.Combine(_exportTargetPath, Identities.Frodo, "registrations"), Does.Exist);
        Assert.That(Path.Combine(_exportTargetPath, Identities.Frodo, "payloads"), Does.Exist);
    }

    //

    [Test]
    public async Task ItShouldEnableAndDisableATenant()
    {
        using var adminClient = CreateAdminClient();
        using var tenantClient = Host.CreateAnonymousClient(Identities.Frodo);

        // The original wrapped each step in a bare { } block so it could redeclare request/response.
        // Two local functions say the same thing and let the test read as the script it is.
        async Task AssertTenantAnswers(HttpStatusCode expected) =>
            Assert.That((await tenantClient.GetAsync("api/owner/v1/authentication/verifyToken")).StatusCode,
                Is.EqualTo(expected));

        async Task AssertAdminPatchOk(string relativePath) =>
            Assert.That((await SendAsync(adminClient, HttpMethod.Patch, relativePath)).StatusCode,
                Is.EqualTo(HttpStatusCode.OK));

        await AssertTenantAnswers(HttpStatusCode.OK);

        await AssertAdminPatchOk($"tenants/{Identities.Frodo}/enable");
        await AssertTenantAnswers(HttpStatusCode.OK);

        await AssertAdminPatchOk($"tenants/{Identities.Frodo}/disable");
        await AssertTenantAnswers(HttpStatusCode.Conflict);

        // Disabled tenants are still returned in the tenant list.
        var tenants = await GetOkAsync<List<TenantModel>>(adminClient, "tenants");
        Assert.That(tenants, Has.Some.Matches<TenantModel>(t => t.Domain == Identities.Frodo));

        await AssertAdminPatchOk($"tenants/{Identities.Frodo}/enable");
        await AssertTenantAnswers(HttpStatusCode.OK);
    }

    //

    [Test]
    public async Task ItShouldEnableAndDisablePublicWebPresence()
    {
        using var adminClient = CreateAdminClient();
        using var tenantClient = Host.CreateAnonymousClient(Identities.Frodo);

        // Same treatment as the sibling test: the original's bare { } blocks existed only to
        // redeclare request/response, and hid an eight-step script behind 80 lines.
        async Task<string> BodyOf(string path, HttpStatusCode expected)
        {
            var response = await tenantClient.GetAsync(path);
            Assert.That(response.StatusCode, Is.EqualTo(expected), $"GET {path}");
            return await response.Content.ReadAsStringAsync();
        }

        async Task AssertWebPresenceFlag(bool expected) =>
            Assert.That((await GetOkAsync<TenantModel>(adminClient, $"tenants/{Identities.Frodo}"))
                .EnablePublicWebPresence, Is.EqualTo(expected));

        async Task AssertAdminPatchOk(string relativePath) =>
            Assert.That((await SendAsync(adminClient, HttpMethod.Patch, relativePath)).StatusCode,
                Is.EqualTo(HttpStatusCode.OK));

        // Enabled by default: public pages render normally.
        await AssertWebPresenceFlag(true);
        Assert.That(await BodyOf("/ssr/home", HttpStatusCode.OK), Does.Not.Contain("This is a Homebase ID."));
        Assert.That(await BodyOf("/robots.txt", HttpStatusCode.OK), Does.Contain("Sitemap:"));
        await BodyOf("/sitemap.xml", HttpStatusCode.OK);

        // Disabled: the pages are gated.
        await AssertAdminPatchOk($"tenants/{Identities.Frodo}/public-web-presence/disable");
        await AssertWebPresenceFlag(false);

        var gatedHome = await BodyOf("/ssr/home", HttpStatusCode.OK);
        Assert.That(gatedHome, Does.Contain("This is a Homebase ID."));
        Assert.That(gatedHome, Does.Contain("noindex"));

        var gatedRobots = await BodyOf("/robots.txt", HttpStatusCode.OK);
        Assert.That(gatedRobots, Does.Contain("Disallow: /"));
        Assert.That(gatedRobots, Does.Not.Contain("Sitemap:"));

        await BodyOf("/sitemap.xml", HttpStatusCode.NotFound);

        // Re-enabled: the pages are back.
        await AssertAdminPatchOk($"tenants/{Identities.Frodo}/public-web-presence/enable");
        Assert.That(await BodyOf("/ssr/home", HttpStatusCode.OK), Does.Not.Contain("This is a Homebase ID."));
    }

    //

    [Test]
    public async Task ItShouldReturnNotFoundTogglingPublicWebPresenceOnUnknownTenant()
    {
        using var adminClient = CreateAdminClient();
        var request = NewRequestMessage(HttpMethod.Patch,
            AdminUrl("tenants/no-such.dotyou.cloud/public-web-presence/disable"));
        var response = await adminClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    //

    private static Guid JobIdFrom(string location) =>
        Guid.Parse(location[(location.LastIndexOf('/') + 1)..]);

    //

    /// <summary>
    /// A channel drive holding one file with a payload, so the storage figures are non-zero.
    /// </summary>
    /// <summary>
    /// Uploads one encrypted payload for Frodo. Only the upload is per-test: <see cref="ResetBetweenTests"/>
    /// wipes the payload tree, so the file genuinely has to be re-uploaded, but the session and the
    /// drive it uploads to come from the baseline.
    /// </summary>
    private Task CreatePayload() =>
        // The literal this replaced was copied from DrivePayloadTests and is exactly what
        // SampleMetadataData.Create(200, allowDistribution: true) builds -- same FileType, null
        // GroupId and Tags, OwnerOnly ACL. The empty AppData.Content the remarks note is preserved:
        // Create leaves it empty, unlike CreateWithContent.
        AppFileUploads.UploadEncryptedAsync(_frodo, _payloadDrive,
            SampleMetadataData.Create(fileType: 200, allowDistribution: true),
            "What is happening with the encoding!?");
}
