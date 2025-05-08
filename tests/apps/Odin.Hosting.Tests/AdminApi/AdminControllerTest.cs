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
using Odin.Services.Admin.Tenants.Jobs;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Core.Storage.Database.System.Table;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
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
        _scaffold.RunBeforeAnyTests(envOverrides: env);

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
        var apiClient = WebScaffold.CreateDefaultHttpClient();
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
    public async Task ItShouldGetSpecificTenant()
    {
        var apiClient = WebScaffold.CreateDefaultHttpClient();
        var request = NewRequestMessage(HttpMethod.Get,
            "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud");
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var tenant = OdinSystemSerializer.Deserialize<TenantModel>(await response.Content.ReadAsStringAsync());
        Assert.That(tenant.Domain, Is.EqualTo("frodo.dotyou.cloud"));
        Assert.That(tenant.RegistrationPath, Does.StartWith(_tenantDataRootPath));
        Assert.That(tenant.RegistrationPath, Does.EndWith(tenant.Id));
        Assert.That(Directory.Exists(tenant.RegistrationPath), Is.True);
        Assert.That(tenant.RegistrationSize, Is.GreaterThan(0));
        Assert.That(tenant.PayloadPath, Is.Null);
        Assert.That(tenant.PayloadSize, Is.Null);
    }

    //

    [Test]
    public async Task ItShouldGetSpecificTenantWithNonExistingPayloads()
    {
        var apiClient = WebScaffold.CreateDefaultHttpClient();
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
        Assert.That(tenant.RegistrationSize, Is.GreaterThan(0));
        Assert.That(tenant.PayloadPath, Is.EqualTo(pm.PayloadsPath));
        Assert.That(tenant.PayloadSize, Is.Not.Null.And.EqualTo(0));

    }

    //

    [Test]
    public async Task ItShouldGetSpecificTenantWithExistingPayload()
    {
        await CreatePayload(TestIdentities.Frodo);

        var apiClient = WebScaffold.CreateDefaultHttpClient();
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
        Assert.That(tenant.PayloadPath, Is.EqualTo(pm.PayloadsPath));
        Assert.That(tenant.RegistrationSize, Is.GreaterThan(0));
        Assert.That(tenant.PayloadPath, Does.StartWith(_tenantDataRootPath));
        Assert.That(tenant.PayloadSize, Is.Not.Null.And.GreaterThan(0));
    }

    //

    [Test]
    public async Task 
        ItShouldDeleteTenant()
    {
        await CreatePayload(TestIdentities.Frodo);

        var url = "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud";
        var apiClient = WebScaffold.CreateDefaultHttpClient();
        var request = NewRequestMessage(HttpMethod.Delete, url);
        var response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        ClassicAssert.IsTrue(response.Headers.TryGetValues("Location", out var locations), "could not find Location header");
        var location = locations.First();
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
        var deleted = await jobManager.DeleteJobAsync(jobId);
        Assert.That(deleted, Is.True);

        exists = await jobManager.JobExistsAsync(jobId);
        Assert.That(exists, Is.False);
        deleted = await jobManager.DeleteJobAsync(jobId);
        Assert.That(deleted, Is.False);

        request = NewRequestMessage(HttpMethod.Get, location);
        response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    //

    [Test]
    public async Task ItShouldExportTenant()
    {
        await CreatePayload(TestIdentities.Frodo);

        var url = "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud/export";
        var apiClient = WebScaffold.CreateDefaultHttpClient();
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
        var deleted = await jobManager.DeleteJobAsync(jobId);
        Assert.That(deleted, Is.True);

        exists = await jobManager.JobExistsAsync(jobId);
        Assert.That(exists, Is.False);
        deleted = await jobManager.DeleteJobAsync(jobId);
        Assert.That(deleted, Is.False);

        request = NewRequestMessage(HttpMethod.Get, location);
        response = await apiClient.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        ClassicAssert.IsTrue(Directory.Exists(Path.Combine(_exportTargetPath, "frodo.dotyou.cloud", "registrations")));
        ClassicAssert.IsTrue(Directory.Exists(Path.Combine(_exportTargetPath, "frodo.dotyou.cloud", "payloads")));
    }

    //

    [Test]
    public async Task ItShouldEnableAndDisableATenant()
    {
        var apiClient = WebScaffold.CreateDefaultHttpClient();

        // Verify enabled
        {
            var request = NewRequestMessage(HttpMethod.Get, $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/api/owner/v1/authentication/verifyToken");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Enable
        {
            var request = NewRequestMessage(HttpMethod.Patch, "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud/enable");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Verify still enabled
        {
            var request = NewRequestMessage(HttpMethod.Get, $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/api/owner/v1/authentication/verifyToken");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Disable
        {
            var request = NewRequestMessage(HttpMethod.Patch, "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud/disable");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Verify disabled
        {
            var request = NewRequestMessage(HttpMethod.Get, $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/api/owner/v1/authentication/verifyToken");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        }

        // Disabled tenants should still be returned in the tenant list
        {
            var request = NewRequestMessage(HttpMethod.Get,
                "https://admin.dotyou.cloud:4444/api/admin/v1/tenants");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var tenants = OdinSystemSerializer.Deserialize<List<TenantModel>>(await response.Content.ReadAsStringAsync());
            Assert.That(tenants, Has.Some.Matches<TenantModel>(t => t.Domain == "frodo.dotyou.cloud"));
        }

        // Enable
        {
            var request = NewRequestMessage(HttpMethod.Patch, "https://admin.dotyou.cloud:4444/api/admin/v1/tenants/frodo.dotyou.cloud/enable");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        // Verify enabled
        {
            var request = NewRequestMessage(HttpMethod.Get, $"https://frodo.dotyou.cloud:{WebScaffold.HttpsPort}/api/owner/v1/authentication/verifyToken");
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
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