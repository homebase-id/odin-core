#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Npgsql;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Hosting.Authentication.Owner;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests.OwnerApi.Authentication;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.Management;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Refit;

namespace Odin.Hosting.Tests.LoadBalancer;

/// <summary>
/// Probes a two-node cluster for load-balancer readiness. Both nodes must already be running
/// against ONE Postgres, ONE Redis and a shared tenant data root, node A on 8443 and node B on
/// 8444 (see scratchpad/lb/node.sh). Nothing here boots a host: these are questions you can only
/// ask of two live processes.
///
/// Each test names the property a load balancer needs. A failure is a place the design breaks
/// today, not a flaky test.
/// </summary>
[Explicit("requires two live nodes sharing postgres + redis; see docs/load-balancing.md")]
public class LoadBalancerProbeTests
{
    private const int NodeA = 8443;
    private const int NodeB = 8444;
    private const string Password = "lb-probe-pwd-8827";
    private static readonly OdinId Frodo = (OdinId)"frodo.dotyou.cloud";

    private ClientAuthenticationToken _token = null!;
    private byte[] _sharedSecret = null!;

    [OneTimeSetUp]
    public async Task LoginOnNodeA()
    {
        await EnsurePasswordAsync(Frodo, NodeA, Password);
        var (token, secret) = await LoginAsync(Frodo, NodeA, Password);
        _token = token;
        _sharedSecret = secret.GetKey();
    }

    /// <summary>The session a client got from one node must be usable on the next request, which
    /// the balancer may route to any node.</summary>
    [Test]
    public async Task Session_FromNodeA_IsAcceptedByNodeB()
    {
        var onA = await GetDrivesAsync(NodeA);
        Assert.That(onA.IsSuccessStatusCode, Is.True, $"sanity: node A rejected its own session ({onA.StatusCode})");

        var onB = await GetDrivesAsync(NodeB);
        Assert.That(onB.IsSuccessStatusCode, Is.True,
            $"node B rejected a session issued by node A ({onB.StatusCode}); owner sessions are not portable");
    }

    /// <summary>A drive created through one node must exist for a client that lands on the other.</summary>
    [Test]
    public async Task DriveCreatedOnNodeA_IsVisibleOnNodeB()
    {
        var drive = TargetDrive.NewTargetDrive();
        var created = await DriveManagerFor(NodeA).CreateDrive(new CreateDriveRequest
        {
            TargetDrive = drive,
            Name = "lb probe " + drive.Alias,
            Metadata = "",
            AllowAnonymousReads = false,
        });
        Assert.That(created.IsSuccessStatusCode, Is.True, $"create on A failed: {created.StatusCode}");

        var onB = await GetDrivesAsync(NodeB);
        Assert.That(onB.IsSuccessStatusCode, Is.True, $"list on B failed: {onB.StatusCode}");
        Assert.That(onB.Content!.Results.Any(d => d.TargetDriveInfo == drive), Is.True,
            "node B does not see a drive created on node A");
    }

    /// <summary>A file written through one node must be readable through the other.</summary>
    [Test]
    public async Task FileUploadedOnNodeA_IsReadableOnNodeB()
    {
        var drive = TargetDrive.NewTargetDrive();
        var created = await DriveManagerFor(NodeA).CreateDrive(new CreateDriveRequest
        {
            TargetDrive = drive,
            Name = "lb file probe",
            Metadata = "",
            AllowAnonymousReads = false,
        });
        Assert.That(created.IsSuccessStatusCode, Is.True, $"create on A failed: {created.StatusCode}");

        var uniqueId = Guid.NewGuid();
        var metadata = SampleMetadataData.Create(fileType: 4242, acl: AccessControlList.OwnerOnly);
        metadata.AppData.UniqueId = uniqueId;
        metadata.AppData.Content = "written via node A";

        var upload = await new UniversalDriveApiClient(Frodo, FactoryFor(NodeA))
            .UploadNewMetadata(drive, metadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"upload on A failed: {upload.StatusCode}");

        var query = await new UniversalDriveApiClient(Frodo, FactoryFor(NodeB)).QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = drive, ClientUniqueIdAtLeastOne = [uniqueId] },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10, IncludeMetadataHeader = true },
        });

        Assert.That(query.IsSuccessStatusCode, Is.True, $"query on B failed: {query.StatusCode}");
        Assert.That(query.Content!.SearchResults.Count(), Is.EqualTo(1),
            "node B cannot read a file uploaded through node A");
    }

    /// <summary>The bytes of a payload, unlike a header, do not live in the database. Uploading
    /// through one node and downloading through the other is what proves blob storage is actually
    /// shared (S3 or a shared mount) rather than sitting on the uploading node's local disk.</summary>
    [Test]
    public async Task PayloadUploadedOnNodeA_IsDownloadableFromNodeB()
    {
        var drive = TargetDrive.NewTargetDrive();
        var created = await DriveManagerFor(NodeA).CreateDrive(new CreateDriveRequest
        {
            TargetDrive = drive,
            Name = "lb payload probe",
            Metadata = "",
            AllowAnonymousReads = false,
        });
        Assert.That(created.IsSuccessStatusCode, Is.True, $"create on A failed: {created.StatusCode}");

        var payloadBytes = Encoding.UTF8.GetBytes("payload written through node A " + Guid.NewGuid());
        var payload = new TestPayloadDefinition
        {
            Key = "lbprobe1",
            ContentType = "text/plain",
            Content = payloadBytes,
            Thumbnails = [],
        };
        var metadata = SampleMetadataData.Create(fileType: 4243, acl: AccessControlList.OwnerOnly);

        var upload = await new UniversalDriveApiClient(Frodo, FactoryFor(NodeA)).UploadNewFile(
            drive,
            metadata,
            new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] },
            [payload]);
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"upload on A failed: {upload.StatusCode}");

        var download = await new UniversalDriveApiClient(Frodo, FactoryFor(NodeB))
            .GetPayload(upload.Content!.File, payload.Key);

        Assert.That(download.IsSuccessStatusCode, Is.True,
            $"node B could not download a payload uploaded through node A ({download.StatusCode}); " +
            "blob storage is not shared between the nodes");
        var received = await download.Content!.ReadAsByteArrayAsync();
        Assert.That(received, Is.EqualTo(payloadBytes), "node B returned different bytes than node A stored");
    }

    /// <summary>Disabling a tenant is an operational kill switch; it must take effect cluster-wide.</summary>
    [Test]
    public async Task TenantDisabledOnNodeA_IsAlsoBlockedOnNodeB()
    {
        using var admin = AdminClient();
        try
        {
            var disable = await admin.PatchAsync(AdminUrl(4444, "tenants/frodo.dotyou.cloud/disable"), null);
            Assert.That(disable.IsSuccessStatusCode, Is.True, "disable on node A failed");

            await Task.Delay(2000);

            var a = await PingAsync(NodeA);
            var b = await PingAsync(NodeB);
            Assert.That(a, Is.Not.EqualTo(HttpStatusCode.OK), "sanity: node A still serves a tenant it disabled");
            Assert.That(b, Is.Not.EqualTo(HttpStatusCode.OK),
                "node B keeps serving a tenant disabled on node A: the identity registry is a per-process cache");
        }
        finally
        {
            await admin.PatchAsync(AdminUrl(4444, "tenants/frodo.dotyou.cloud/enable"), null);
            await admin.PatchAsync(AdminUrl(4445, "tenants/frodo.dotyou.cloud/enable"), null);
        }
    }

    /// <summary>The sweep is the guarantee behind the change notifications, so it has to be proven
    /// without them. This changes the registration directly in the database, which no node
    /// publishes, and asserts both nodes converge anyway.</summary>
    [Test]
    public async Task RegistryChangeMadeOnlyInTheDatabase_ConvergesOnBothNodes()
    {
        var connectionString = Environment.GetEnvironmentVariable("ODIN_LB_PG")
                               ?? "Host=localhost;Port=54320;Database=homebase_dev;Username=homebase_dev;Password=homebase_dev";

        await SetDisabledInDatabaseAsync(connectionString, disabled: true);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                if (await PingAsync(NodeA) != HttpStatusCode.OK && await PingAsync(NodeB) != HttpStatusCode.OK)
                {
                    return;
                }

                await Task.Delay(2000);
            }

            Assert.Fail("nodes did not converge on a database-only registry change; " +
                        "the reconciliation sweep is not running or not detecting updates");
        }
        finally
        {
            // Wait for the cluster to come back before returning: the sweep takes up to an interval,
            // and leaving the tenant disabled would fail whichever test runs next.
            await SetDisabledInDatabaseAsync(connectionString, disabled: false);
            await WaitUntilServingAsync(TimeSpan.FromSeconds(90));
        }
    }

    private static async Task WaitUntilServingAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (await PingAsync(NodeA) == HttpStatusCode.OK && await PingAsync(NodeB) == HttpStatusCode.OK)
            {
                return;
            }

            await Task.Delay(2000);
        }

        Assert.Fail("nodes did not resume serving the tenant after it was re-enabled in the database");
    }

    private static async Task SetDisabledInDatabaseAsync(string connectionString, bool disabled)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "update registrations set disabled = @disabled where primarydomainname = @domain";
        command.Parameters.AddWithValue("disabled", disabled);
        command.Parameters.AddWithValue("domain", Frodo.DomainName);
        await command.ExecuteNonQueryAsync();
    }

    // ---------- plumbing ----------

    private IApiClientFactory FactoryFor(int port) => new NodeApiClientFactory(_token, _sharedSecret, port);

    // IRefitDriveManagement declares absolute paths, so its base address must be the host root,
    // not the /api/owner/v1 prefix the _Universal clients expect.
    private IRefitDriveManagement DriveManagerFor(int port)
    {
        var client = FactoryFor(port).CreateHttpClient(Frodo, out var secret);
        client.BaseAddress = new Uri($"https://{Frodo}:{port}");
        return RefitCreator.RestServiceFor<IRefitDriveManagement>(client, secret);
    }

    private Task<ApiResponse<PagedResult<OwnerClientDriveData>>> GetDrivesAsync(int port)
        => DriveManagerFor(port).GetDrives(new GetDrivesRequest { PageNumber = 1, PageSize = 100 });

    private static async Task<HttpStatusCode> PingAsync(int port)
    {
        using var client = RawClient();
        var response = await client.GetAsync($"https://{Frodo}:{port}/api/v2/health/ping");
        return response.StatusCode;
    }

    private static string AdminUrl(int port, string path) => $"https://admin.dotyou.cloud:{port}/api/admin/v1/{path}";

    private static HttpClient AdminClient()
    {
        var client = RawClient();
        client.DefaultRequestHeaders.Add("Odin-Admin-Api-Key", "lb-test-key");
        return client;
    }

    private static HttpClient RawClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    private static async Task EnsurePasswordAsync(OdinId identity, int port, string password)
    {
        using var client = RawClient();
        client.BaseAddress = new Uri($"https://{identity}:{port}");
        var svc = RestService.For<IOwnerAuthenticationClient>(client);

        var probe = await client.PostAsync($"{OwnerApiPathConstants.AuthV1}/ispasswordset", null);
        if (probe.IsSuccessStatusCode && (await probe.Content.ReadAsStringAsync()).Trim() == "true")
        {
            return;
        }

        var ecc = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        var salts = await svc.GenerateNewSalts();
        Assert.That(salts.IsSuccessStatusCode, Is.True, "GenerateNewSalts failed");
        var nonce = new NonceData(salts.Content!.SaltPassword64, salts.Content.SaltKek64, salts.Content.PublicJwk, salts.Content.CRC)
        {
            Nonce64 = salts.Content.Nonce64,
        };
        var reply = PasswordDataManager.CalculatePasswordReply(password, nonce, ecc);
        var set = await svc.SetNewPassword(reply);
        Assert.That(set.IsSuccessStatusCode, Is.True, "SetNewPassword failed");
    }

    private static async Task<(ClientAuthenticationToken token, SensitiveByteArray secret)> LoginAsync(
        OdinId identity, int port, string password)
    {
        var jar = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = jar,
            UseCookies = true,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        using var client = new HttpClient(handler) { BaseAddress = new Uri($"https://{identity}:{port}") };
        var svc = RestService.For<IOwnerAuthenticationClient>(client);

        var ecc = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
        var nonceResponse = await svc.GenerateAuthenticationNonce();
        Assert.That(nonceResponse.IsSuccessStatusCode, Is.True, "GenerateAuthenticationNonce failed");
        var n = nonceResponse.Content!;
        var nonce = new NonceData(n.SaltPassword64, n.SaltKek64, n.PublicJwk, n.CRC) { Nonce64 = n.Nonce64 };
        var reply = PasswordDataManager.CalculatePasswordReply(password, nonce, ecc);

        var authenticated = await svc.Authenticate(reply);
        Assert.That(authenticated.IsSuccessStatusCode, Is.True, $"Authenticate on port {port} failed");

        var cookie = HttpUtility.UrlDecode(jar.GetCookies(client.BaseAddress!)[OwnerAuthConstants.CookieName]?.Value);
        Assert.That(ClientAuthenticationToken.TryParse(cookie, out var token), Is.True, "no owner cookie returned");
        return (token, authenticated.Content!.SharedSecret.ToSensitiveByteArray());
    }

    /// <summary>Copy of OwnerApiClientFactory that can target a specific node's port.</summary>
    private sealed class NodeApiClientFactory(ClientAuthenticationToken token, byte[] secret, int port) : IApiClientFactory
    {
        public SensitiveByteArray SharedSecret { get; } = secret.ToSensitiveByteArray();

        public HttpClient CreateHttpClient(OdinId identity, out SensitiveByteArray sharedSecret,
            FileSystemType fileSystemType = FileSystemType.Standard)
        {
            var client = WebScaffold.HttpClientFactory.CreateClient(
                $"{nameof(NodeApiClientFactory)}:{identity}:{port}",
                config => config.MessageHandlerChain.Add(inner => new SharedSecretGetRequestHandler(inner)));

            var cookieValue = $"{OwnerAuthConstants.CookieName}={token}";
            client.DefaultRequestHeaders.Add("Cookie", cookieValue);
            client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
            client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(secret));
            client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(typeof(FileSystemType), fileSystemType));
            client.Timeout = TimeSpan.FromMinutes(5);
            client.BaseAddress = new Uri($"https://{identity}:{port}{OwnerApiPathConstants.BasePathV1}");

            sharedSecret = secret.ToSensitiveByteArray();
            return client;
        }
    }
}
