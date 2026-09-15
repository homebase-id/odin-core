using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Serialization;
using Odin.Services.Admin.Tenants;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.System.Table;
using Odin.Core.Storage.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Services.Admin.Tenants.Jobs;
using Odin.Services.Configuration;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.JobManagement;

namespace Odin.Hosting.Tests.AdminApi;

[CancelAfter(60000)]
public class AdminControllerTest
{
    private WebScaffold _scaffold = null!;
    private string _tenantDataRootPath;
    private OdinConfiguration _config = null!;
    private readonly string _exportTargetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

    [SetUp]
    public void Init()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        var env = new Dictionary<string, string>
        {
            { "Admin__ApiEnabled", "true" },
            { "Admin__ApiKey", "your-secret-api-key-here" },
            { "Admin__ApiKeyHttpHeaderName", "Odin-Admin-Api-Key" },
            { "Admin__ApiPort", "4444" },
            { "Admin__Domain", "admin.dotyou.cloud" },
            { "Admin__ExportTargetPath", _exportTargetPath },
        };
        _scaffold.RunBeforeAnyTests(envOverrides: env, testIdentities: new List<TestIdentity>() { TestIdentities.Frodo });

        _tenantDataRootPath = Environment.GetEnvironmentVariable("Host__TenantDataRootPath") ?? "";
        Assert.That(_tenantDataRootPath, Is.Not.Empty);
        Assert.That(Directory.Exists(_tenantDataRootPath));

        _config = _scaffold.Services.GetService<OdinConfiguration>();
    }

    //

    [TearDown]
    public void Cleanup()
    {
        if (Directory.Exists(_exportTargetPath))
        {
            Directory.Delete(_exportTargetPath, true);
        }
        _scaffold.RunAfterAnyTests();
    }

    //

    private static HttpRequestMessage NewRequestMessage(HttpMethod method, string uri)
    {
        return new HttpRequestMessage(method, uri)
        {
            Headers = { { "Odin-Admin-Api-Key", "your-secret-api-key-here" } }
        };
    }

    //

    [Test]
    public async Task ItShouldGetAllTenants()
    {
        var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
        var request = NewRequestMessage(HttpMethod.Get,
            "https://admin.dotyou.cloud:4444/api/admin/v1/tenants");
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var tenants = OdinSystemSerializer.Deserialize<List<TenantModel>>(await response.Content.ReadAsStringAsync());
        Assert.That(tenants.Count, Is.GreaterThan(1));
        Assert.That(tenants, Has.Some.Matches<TenantModel>(t => t.Domain == "frodo.dotyou.cloud"));
    }

    //

    [Test]
    public async Task ItShouldGetTenantMetrics()
    {
        var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
        var request = NewRequestMessage(HttpMethod.Get,
            "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/metrics");
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var metrics = OdinSystemSerializer.Deserialize<TenantMetricsResponse>(
            await response.Content.ReadAsStringAsync());

        Assert.That(metrics.GeneratedAt.milliseconds, Is.GreaterThan(0));
        Assert.That(metrics.DatabaseType, Is.EqualTo(_config.Database.Type.ToString().ToLowerInvariant()));
        // The cross-tenant scan needs one shared database; SQLite gives each tenant its own file.
        Assert.That(metrics.IndexOrphanScanSupported,
            Is.EqualTo(_config.Database.Type == DatabaseType.Postgres));

        var frodo = metrics.Tenants.SingleOrDefault(t => t.Domain == "frodo.dotyou.cloud");
        Assert.That(frodo, Is.Not.Null);
        Assert.That(frodo.Registered, Is.True);
        Assert.That(frodo.OrphanSource, Is.Null);
        Assert.That(frodo.Enabled, Is.True);
        Assert.That(frodo.DriveCount, Is.Not.Null.And.GreaterThan(0));
        Assert.That(frodo.Files, Is.Not.Null);
        Assert.That(frodo.ActiveBytes, Is.Not.Null.And.LessThanOrEqualTo(frodo.TotalBytes));

        // The id must be a canonical UUID string, and the same one the tenant list reports.
        Assert.That(Guid.TryParse(frodo.Id, out _), Is.True);

        var tenantRequest = NewRequestMessage(HttpMethod.Get,
            "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud");
        var tenantResponse = await apiClient.SendAsync(tenantRequest);
        var tenant = OdinSystemSerializer.Deserialize<TenantModel>(
            await tenantResponse.Content.ReadAsStringAsync());
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
        await CreatePayload(TestIdentities.Frodo);

        var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");

        var response = await apiClient.SendAsync(NewRequestMessage(HttpMethod.Get,
            "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/metrics"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var metrics = OdinSystemSerializer.Deserialize<TenantMetricsResponse>(
            await response.Content.ReadAsStringAsync());
        var frodo = metrics.Tenants.Single(t => t.Domain == "frodo.dotyou.cloud");

        Assert.That(frodo.Files, Is.Not.Null.And.GreaterThan(0));
        Assert.That(frodo.TotalBytes, Is.Not.Null.And.GreaterThan(0));
        Assert.That(frodo.ActiveBytes, Is.Not.Null.And.GreaterThan(0));
        Assert.That(frodo.DriveCount, Is.Not.Null.And.GreaterThan(0));

        // Nothing was deleted, so every byte is live.
        Assert.That(frodo.ActiveBytes, Is.EqualTo(frodo.TotalBytes));

        // Same figure the existing endpoint reports: both sum byteCount over every file state.
        var payloadResponse = await apiClient.SendAsync(NewRequestMessage(HttpMethod.Get,
            "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud?include-payload=true"));
        var tenant = OdinSystemSerializer.Deserialize<TenantModel>(
            await payloadResponse.Content.ReadAsStringAsync());
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
        await CreatePayload(TestIdentities.Frodo);

        var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");

        var metrics = OdinSystemSerializer.Deserialize<TenantMetricsResponse>(
            await (await apiClient.SendAsync(NewRequestMessage(HttpMethod.Get,
                "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/metrics"))).Content.ReadAsStringAsync());
        var frodoMetrics = metrics.Tenants.Single(t => t.Domain == "frodo.dotyou.cloud");

        var tenant = OdinSystemSerializer.Deserialize<TenantModel>(
            await (await apiClient.SendAsync(NewRequestMessage(HttpMethod.Get,
                "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud?include-payload=true"))).Content.ReadAsStringAsync());

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
        var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");

        var metricsResponse = await apiClient.SendAsync(NewRequestMessage(HttpMethod.Get,
            "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/metrics"));
        Assert.That(metricsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var metrics = OdinSystemSerializer.Deserialize<TenantMetricsResponse>(
            await metricsResponse.Content.ReadAsStringAsync());
        Assert.That(metrics.Tenants, Is.Not.Empty);

        var tenantResponse = await apiClient.SendAsync(NewRequestMessage(HttpMethod.Get,
            "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud"));
        Assert.That(tenantResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var tenant = OdinSystemSerializer.Deserialize<TenantModel>(
            await tenantResponse.Content.ReadAsStringAsync());
        Assert.That(tenant.Domain, Is.EqualTo("frodo.dotyou.cloud"));
    }

    //

    [Test]
    public async Task ItShouldGetSpecificTenant()
    {
        var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
        var request = NewRequestMessage(HttpMethod.Get,
            "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud");
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var tenant = OdinSystemSerializer.Deserialize<TenantModel>(await response.Content.ReadAsStringAsync());
        Assert.That(tenant.Domain, Is.EqualTo("frodo.dotyou.cloud"));
        Assert.That(tenant.RegistrationPath, Does.StartWith(_tenantDataRootPath));
        Assert.That(tenant.RegistrationPath, Does.EndWith(tenant.Id));
        Assert.That(Directory.Exists(tenant.RegistrationPath), Is.True);

        if (_config.Database.Type == DatabaseType.Sqlite)
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
        var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
        var request = NewRequestMessage(HttpMethod.Get,
            "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud?include-payload=true");
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var tenant = OdinSystemSerializer.Deserialize<TenantModel>(await response.Content.ReadAsStringAsync());
        Assert.That(tenant.Id, Is.Not.Null);
        Assert.That(tenant.Id, Is.Not.EqualTo(Guid.Empty));

        var pm = new TenantPathManager(_config, Guid.Parse(tenant.Id));

        Assert.That(tenant.Domain, Is.EqualTo("frodo.dotyou.cloud"));
        Assert.That(tenant.RegistrationPath, Does.StartWith(_tenantDataRootPath));
        Assert.That(tenant.RegistrationPath, Does.EndWith(tenant.Id));
        Assert.That(Directory.Exists(tenant.RegistrationPath), Is.True);

        if (_config.Database.Type == DatabaseType.Sqlite)
        {
            Assert.That(tenant.RegistrationSize, Is.GreaterThan(0));
        }

#if RUN_S3_TESTS
        var serviceUrl = Environment.GetEnvironmentVariable("S3Storage__ServiceUrl") ?? "";
        var bucketName = Environment.GetEnvironmentVariable("S3Payload__BucketName") ?? "";
        Assert.That(tenant.PayloadPath, Is.EqualTo(Path.Combine(serviceUrl, bucketName, tenant.Id)));
#else
        Assert.That(tenant.PayloadPath, Is.EqualTo(pm.PayloadsPath));
#endif

        Assert.That(tenant.PayloadSize, Is.Not.Null.And.EqualTo(0));
    }

    //

    [Test]
    public async Task ItShouldGetSpecificTenantWithExistingPayload()
    {
        await CreatePayload(TestIdentities.Frodo);

        var apiClient = WebScaffold.HttpClientFactory.CreateClient($"admin.dotyou.cloud:4444");
        var request = NewRequestMessage(HttpMethod.Get,
            "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud?include-payload=true");
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var tenant = OdinSystemSerializer.Deserialize<TenantModel>(await response.Content.ReadAsStringAsync());

        Assert.That(tenant.Id, Is.Not.Null);
        Assert.That(tenant.Id, Is.Not.EqualTo(Guid.Empty));

        var pm = new TenantPathManager(_config, Guid.Parse(tenant.Id));

        Assert.That(tenant.Domain, Is.EqualTo("frodo.dotyou.cloud"));
        Assert.That(tenant.RegistrationPath, Does.StartWith(_tenantDataRootPath));
        Assert.That(tenant.RegistrationPath, Does.EndWith(tenant.Id));

        if (_config.Database.Type == DatabaseType.Sqlite)
        {
            Assert.That(tenant.RegistrationSize, Is.GreaterThan(0));
        }

#if RUN_S3_TESTS
        var serviceUrl = Environment.GetEnvironmentVariable("S3Storage__ServiceUrl") ?? "";
        var bucketName = Environment.GetEnvironmentVariable("S3Payload__BucketName") ?? "";
        Assert.That(tenant.PayloadPath, Is.EqualTo(Path.Combine(serviceUrl, bucketName, tenant.Id)));
#else
        Assert.That(tenant.PayloadPath, Is.EqualTo(pm.PayloadsPath));
        Assert.That(tenant.PayloadPath, Does.StartWith(_tenantDataRootPath));
#endif

        Assert.That(tenant.PayloadSize, Is.Not.Null.And.GreaterThan(0));
    }

    //

// SEB:TODO disabled until we figure out a way to do this without racing the rest of the system
#if false
    [Test]
    public async Task ItShouldDeleteTenant()
    {
        await CreatePayload(TestIdentities.Frodo);

        var url = "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud";

        var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
        var request = NewRequestMessage(HttpMethod.Delete, url);
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        ClassicAssert.IsTrue(response.Headers.TryGetValues("Location", out var locations), "could not find Location header");
        var location = locations!.First();
        Assert.That(location, Does.StartWith("https://admin.dotyou.cloud:4444/api/job/v1/"));

        var idx = 0;
        const int max = 20;
        var jobResponse = new JobApiResponse();
        for (idx = 0; idx < max; idx++)
        {
            await Task.Delay(100);
            request = NewRequestMessage(HttpMethod.Get, location);
            response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            jobResponse = JobApiResponse.Deserialize(await response.Content.ReadAsStringAsync());
            if (jobResponse.State == JobState.Succeeded)
            {
                break;
            }
        }
        if (idx == max)
        {
            Assert.Fail("Failed to delete tenant");
        }

        Assert.That(jobResponse.JobId, Is.Not.Null);

        var jobManager = _scaffold.Services.GetRequiredService<IJobManager>();
        var jobId = jobResponse.JobId.Value;

        var exists = await jobManager.JobExistsAsync(jobId);
        Assert.That(exists, Is.True);
        var deleted = await jobManager.DeleteJobByIdAsync(jobId);
        Assert.That(deleted, Is.True);

        exists = await jobManager.JobExistsAsync(jobId);
        Assert.That(exists, Is.False);
        deleted = await jobManager.DeleteJobByIdAsync(jobId);
        Assert.That(deleted, Is.False);

        request = NewRequestMessage(HttpMethod.Get, location);
        response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
#endif

    //

#if !RUN_S3_TESTS
    // SEB:TODO update for S3 payloads
    [Test]
    public async Task ItShouldExportTenant()
    {
        await CreatePayload(TestIdentities.Frodo);

        var url = "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud/export";
        var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
        var request = NewRequestMessage(HttpMethod.Post, url);
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        ClassicAssert.IsTrue(response.Headers.TryGetValues("Location", out var locations), "could not find Location header");
        var location = locations.First();
        Assert.That(location, Does.StartWith("https://admin.dotyou.cloud:4444/api/job/v1/"));

        var idx = 0;
        const int max = 20;
        var jobResponse = new JobApiResponse();
        ExportTenantJobData exportData = null;
        for (idx = 0; idx < max; idx++)
        {
            await Task.Delay(100);
            request = NewRequestMessage(HttpMethod.Get, location);
            response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            (jobResponse, exportData) = JobApiResponse.Deserialize<ExportTenantJobData>(await response.Content.ReadAsStringAsync());
            if (jobResponse.State == JobState.Succeeded)
            {
                break;
            }
        }
        if (idx == max)
        {
            Assert.Fail("Failed to export tenant - did not complete");
        }

        Assert.That(jobResponse.JobId, Is.Not.Null);
        Assert.That(exportData?.TargetPath, Is.EqualTo(Path.Combine(_exportTargetPath, "frodo.dotyou.cloud")));

        var jobManager = _scaffold.Services.GetRequiredService<IJobManager>();

        var jobId = jobResponse.JobId.Value;

        var exists = await jobManager.JobExistsAsync(jobId);
        Assert.That(exists, Is.True);
        var deleted = await jobManager.DeleteJobByIdAsync(jobId);
        Assert.That(deleted, Is.True);

        exists = await jobManager.JobExistsAsync(jobId);
        Assert.That(exists, Is.False);
        deleted = await jobManager.DeleteJobByIdAsync(jobId);
        Assert.That(deleted, Is.False);

        request = NewRequestMessage(HttpMethod.Get, location);
        response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        ClassicAssert.IsTrue(Directory.Exists(Path.Combine(_exportTargetPath, "frodo.dotyou.cloud", "registrations")));
        ClassicAssert.IsTrue(Directory.Exists(Path.Combine(_exportTargetPath, "frodo.dotyou.cloud", "payloads")));
    }
#endif

    //

    [Test]
    public async Task ItShouldEnableAndDisableATenant()
    {
        // Verify enabled
        {
            var apiClient = WebScaffold.HttpClientFactory.CreateClient($"frodo.dotyou.cloud:{WebScaffold.HttpsPort}");
            var request = NewRequestMessage(HttpMethod.Get, $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/api/owner/v1/authentication/verifyToken");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Enable
        {
            var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
            var request = NewRequestMessage(HttpMethod.Patch, "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud/enable");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Verify still enabled
        {
            var apiClient = WebScaffold.HttpClientFactory.CreateClient($"frodo.dotyou.cloud:{WebScaffold.HttpsPort}");
            var request = NewRequestMessage(HttpMethod.Get, $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/api/owner/v1/authentication/verifyToken");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Disable
        {
            var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
            var request = NewRequestMessage(HttpMethod.Patch, "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud/disable");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Verify disabled
        {
            var apiClient = WebScaffold.HttpClientFactory.CreateClient($"frodo.dotyou.cloud:{WebScaffold.HttpsPort}");
            var request = NewRequestMessage(HttpMethod.Get, $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/api/owner/v1/authentication/verifyToken");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        }

        // Disabled tenants should still be returned in the tenant list
        {
            var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
            var request = NewRequestMessage(HttpMethod.Get,
                "https://admin.dotyou.cloud:4444/api/admin/v1/tenants");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var tenants = OdinSystemSerializer.Deserialize<List<TenantModel>>(await response.Content.ReadAsStringAsync());
            Assert.That(tenants, Has.Some.Matches<TenantModel>(t => t.Domain == "frodo.dotyou.cloud"));
        }

        // Enable
        {
            var apiClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
            var request = NewRequestMessage(HttpMethod.Patch, "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud/enable");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Verify enabled
        {
            var apiClient = WebScaffold.HttpClientFactory.CreateClient($"frodo.dotyou.cloud:{WebScaffold.HttpsPort}");
            var request = NewRequestMessage(HttpMethod.Get, $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/api/owner/v1/authentication/verifyToken");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    //

    [Test]
    public async Task ItShouldEnableAndDisablePublicWebPresence()
    {
        var adminClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
        var tenantClient = WebScaffold.HttpClientFactory.CreateClient($"frodo.dotyou.cloud:{WebScaffold.HttpsPort}");

        // Verify enabled by default
        {
            var request = NewRequestMessage(HttpMethod.Get,
                "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud");
            var response = await adminClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var tenant = OdinSystemSerializer.Deserialize<TenantModel>(await response.Content.ReadAsStringAsync());
            Assert.That(tenant.EnablePublicWebPresence, Is.True);
        }

        // Public pages render normally
        {
            var response = await tenantClient.SendAsync(new HttpRequestMessage(HttpMethod.Get,
                $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/ssr/home"));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var body = await response.Content.ReadAsStringAsync();
            Assert.That(body, Does.Not.Contain("This is a Homebase ID."));

            response = await tenantClient.SendAsync(new HttpRequestMessage(HttpMethod.Get,
                $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/robots.txt"));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            body = await response.Content.ReadAsStringAsync();
            Assert.That(body, Does.Contain("Sitemap:"));

            response = await tenantClient.SendAsync(new HttpRequestMessage(HttpMethod.Get,
                $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/sitemap.xml"));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Disable public web presence
        {
            var request = NewRequestMessage(HttpMethod.Patch,
                "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud/public-web-presence/disable");
            var response = await adminClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Verify flag is off
        {
            var request = NewRequestMessage(HttpMethod.Get,
                "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud");
            var response = await adminClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var tenant = OdinSystemSerializer.Deserialize<TenantModel>(await response.Content.ReadAsStringAsync());
            Assert.That(tenant.EnablePublicWebPresence, Is.False);
        }

        // Public pages are gated
        {
            var response = await tenantClient.SendAsync(new HttpRequestMessage(HttpMethod.Get,
                $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/ssr/home"));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var body = await response.Content.ReadAsStringAsync();
            Assert.That(body, Does.Contain("This is a Homebase ID."));
            Assert.That(body, Does.Contain("noindex"));

            response = await tenantClient.SendAsync(new HttpRequestMessage(HttpMethod.Get,
                $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/robots.txt"));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            body = await response.Content.ReadAsStringAsync();
            Assert.That(body, Does.Contain("Disallow: /"));
            Assert.That(body, Does.Not.Contain("Sitemap:"));

            response = await tenantClient.SendAsync(new HttpRequestMessage(HttpMethod.Get,
                $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/sitemap.xml"));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        // Re-enable and verify pages are back
        {
            var request = NewRequestMessage(HttpMethod.Patch,
                "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud/public-web-presence/enable");
            var response = await adminClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            response = await tenantClient.SendAsync(new HttpRequestMessage(HttpMethod.Get,
                $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/ssr/home"));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var body = await response.Content.ReadAsStringAsync();
            Assert.That(body, Does.Not.Contain("This is a Homebase ID."));
        }
    }

    //

    [Test]
    public async Task ItShouldReturnNotFoundTogglingPublicWebPresenceOnUnknownTenant()
    {
        var adminClient = WebScaffold.HttpClientFactory.CreateClient("admin.dotyou.cloud:4444");
        var request = NewRequestMessage(HttpMethod.Patch,
            "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/no-such.dotyou.cloud/public-web-presence/disable");
        var response = await adminClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    //

    private async Task CreatePayload(TestIdentity testIdentity)
    {
        var ownerClient = _scaffold.CreateOwnerApiClient(testIdentity);

        var drive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };
        await ownerClient.Drive.CreateDrive(drive, "A Channel Drive", "", false, false);
        const string uploadedContent = "I'm Mr. Underhill";
        const string uploadedPayload = "What is happening with the encoding!?";
        await UploadStandardFileToChannel(ownerClient, drive, uploadedContent, uploadedPayload);
    }

    // Lifted from Odin.Hosting.Tests.OwnerApi.Drive.StandardFileSystem.DrivePayloadTests
    private async Task<UploadResult> UploadStandardFileToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent, string payload)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            AppData = new()
            {
                FileType = 200,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata, payload, null, null, WebScaffold.PAYLOAD_KEY);
    }
}