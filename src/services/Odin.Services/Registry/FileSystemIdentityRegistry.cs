using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Core.Trie;
using Odin.Core.Util;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Registry.Registration;
using Odin.Services.Tenant.Container;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

namespace Odin.Services.Registry;

/// <summary>
/// Reads identities from the file system using a convention
/// </summary>
public class FileSystemIdentityRegistry : IIdentityRegistry
{
    public string RegistrationRoot { get; private set; }
    public string ShardablePayloadRoot { get; private set; }

    private readonly ILogger<FileSystemIdentityRegistry> _logger;
    private readonly ConcurrentDictionary<Guid, IdentityRegistration> _cache;
    private readonly Trie<IdentityRegistration> _trie;
    private readonly ICertificateServiceFactory _certificateServiceFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISystemHttpClient _systemHttpClient;
    private readonly IMultiTenantContainerAccessor _tenantContainer;
    private readonly OdinConfiguration _config;
    private readonly bool _useCertificateAuthorityProductionServers;
    private readonly string _tempFolderRoot;

    public FileSystemIdentityRegistry(
        ILogger<FileSystemIdentityRegistry> logger,
        ICertificateServiceFactory certificateServiceFactory,
        IHttpClientFactory httpClientFactory,
        ISystemHttpClient systemHttpClient,
        IMultiTenantContainerAccessor tenantContainer,
        OdinConfiguration config
    )
    {
        var tenantDataRootPath = config.Host.TenantDataRootPath;
        RegistrationRoot = Path.Combine(tenantDataRootPath, "registrations");
        ShardablePayloadRoot = Path.Combine(tenantDataRootPath, "payloads");
        _tempFolderRoot = tenantDataRootPath;

        if (!Directory.Exists(tenantDataRootPath))
        {
            throw new InvalidDataException($"Could find or access path at [{tenantDataRootPath}]");
        }

        _cache = new ConcurrentDictionary<Guid, IdentityRegistration>();
        _trie = new Trie<IdentityRegistration>();
        _logger = logger;
        _certificateServiceFactory = certificateServiceFactory;
        _httpClientFactory = httpClientFactory;
        _systemHttpClient = systemHttpClient;
        _tenantContainer = tenantContainer;
        _config = config;

        _useCertificateAuthorityProductionServers = config.CertificateRenewal.UseCertificateAuthorityProductionServers;

        RegisterCertificateInitializerHttpClient();
        Initialize();
    }


    public void Initialize()
    {
        LoadCache();
    }

    public Guid? ResolveId(string domain)
    {
        var reg = _trie.LookupExactName(domain);
        return reg?.Id;
    }

    public IdentityRegistration ResolveIdentityRegistration(string domain, out string prefix)
    {
        if (string.IsNullOrEmpty(domain))
        {
            prefix = "";
            return null;
        }

        var (reg, pre) = _trie.LookupName(domain);

        prefix = pre;
        if (reg == null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(prefix) || DnsConfigurationSet.WellknownPrefixes.Contains(prefix))
        {
            return reg;
        }

        return null;
    }


    public TenantContext CreateTenantContext(string domain, bool updateFileSystem = false)
    {
        var idReg = this.ResolveIdentityRegistration(domain, out _);
        return this.CreateTenantContext(idReg, updateFileSystem);
    }

    public TenantContext CreateTenantContext(IdentityRegistration idReg, bool updateFileSystem = false)
    {
        var regIdFolder = idReg.Id.ToString();

        var rootPath = Path.Combine(RegistrationRoot, regIdFolder);
        var storageConfig = new TenantStorageConfig(
            headerDataStoragePath: Path.Combine(rootPath, "headers"),
            tempStoragePath: Path.Combine(_tempFolderRoot, "temp", regIdFolder),
            payloadStoragePath: Path.Combine(this.ShardablePayloadRoot, idReg.PayloadShardKey, regIdFolder),
            staticFileStoragePath: Path.Combine(rootPath, "static")
        );

        var sslRoot = Path.Combine(rootPath, "ssl");

        // IO is slow, so make it optional
        if (updateFileSystem)
        {
            Directory.CreateDirectory(sslRoot);
            Directory.CreateDirectory(storageConfig.HeaderDataStoragePath);
            Directory.CreateDirectory(storageConfig.TempStoragePath);
            Directory.CreateDirectory(storageConfig.PayloadStoragePath);
            Directory.CreateDirectory(storageConfig.StaticFileStoragePath);
        }

        var isPreconfigured = _config.Development?.PreconfiguredDomains.Any(d => d.Equals(idReg.PrimaryDomainName,
            StringComparison.InvariantCultureIgnoreCase)) ?? false;
        var tc = new TenantContext(idReg.Id, (OdinId)idReg.PrimaryDomainName, sslRoot, storageConfig, idReg.FirstRunToken, isPreconfigured,
            idReg.MarkedForDeletionDate);
        return tc;
    }

    public Task<bool> IsIdentityRegistered(string domain)
    {
        return Task.FromResult(_trie.LookupExactName(domain) != null);
    }

    public async Task<bool> CanAddNewRegistration(string domain)
    {
        if (!_trie.IsDomainUniqueInHierarchy(domain))
        {
            return false;
        }
        var registration = await Get(domain);
        return registration == null;
    }

    public async Task<Guid> AddRegistration(IdentityRegistrationRequest request)
    {
        string GetNextShard()
        {
            //TODO: read folders under this.ShardablePayloadRoot and choose a folder; wisely (maybe round robin?)
            const string shard1 = "shard1";
            return shard1;
        }

        var registration = new IdentityRegistration()
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PlanId = request.PlanId,
            PrimaryDomainName = request.OdinId,
            IsCertificateManaged = request.IsCertificateManaged,
            FirstRunToken = Guid.NewGuid(),
            PayloadShardKey = GetNextShard()
        };

        await SaveRegistrationInternal(registration);

        if (request.OptionalCertificatePemContent == null)
        {
            await InitializeCertificate(request.OdinId);
        }
        else
        {
            //optionally, let an ssl certificate be provided 
            //TODO: is there a way to pull a specific tenant's service config from Autofac?
            var tenantContext = CreateTenantContext(request.OdinId, true);

            var tc = _certificateServiceFactory.Create(tenantContext.SslRoot);
            await tc.SaveSslCertificate(
                request.OdinId.DomainName,
                new KeysAndCertificates
                {
                    CertificatesPem = request.OptionalCertificatePemContent.Certificate,
                    PrivateKeyPem = request.OptionalCertificatePemContent.PrivateKey,
                });
        }

        return registration.FirstRunToken.GetValueOrDefault();
    }

    public async Task DeleteRegistration(string domain)
    {
        var registration = await Get(domain);

        if (null != registration)
        {
            _trie.RemoveDomain(domain);
            UnloadRegistration(registration);
            var tenantRoot = Path.Combine(RegistrationRoot, registration.Id.ToString());
            Directory.Delete(tenantRoot, true);
            await DeletePayloads(registration);
        }
    }

    // Copy registration and payloads
    public async Task<string> CopyRegistration(string domain, string targetRootPath)
    {
        var registration = await Get(domain);
        if (registration == null)
        {
            return "";
        }

        var disabled = registration.Disabled;
        await ToggleDisabled(domain, true);
        try
        {
            var targetPath = Path.Combine(targetRootPath, domain);
            if (Directory.Exists(targetPath))
            {
                throw new OdinClientException($"Path {targetPath} already exists");
            }

            var registrationId = registration.Id.ToString();
            var targetRegistrationsPath = Path.Combine(targetPath, "registrations", registrationId);
            Directory.CreateDirectory(targetRegistrationsPath);

            _logger.LogInformation("Copying {domain} registration to {targetRegistrationsPath}", domain, targetRegistrationsPath);
            var source = new DirectoryInfo(Path.Combine(RegistrationRoot, registrationId));
            await Task.Run(() => source.CopyTo(targetRegistrationsPath));

            var targetPayloadsPath = Path.Combine(targetPath, "payloads");
            Directory.CreateDirectory(targetPayloadsPath);

            var shards = Directory.GetDirectories(ShardablePayloadRoot);
            foreach (var shard in shards)
            {
                var payloadSourcePath = Path.Combine(shard, registrationId);
                var payloadTargetPath = Path.Combine(targetPayloadsPath, Path.GetFileName(shard), registrationId);
                if (Directory.Exists(payloadSourcePath))
                {
                    _logger.LogInformation("Copying {domain} shard to {payloadTargetPath}", domain, payloadTargetPath);
                    source = new DirectoryInfo(Path.Combine(payloadSourcePath));
                    await Task.Run(() => source.CopyTo(payloadTargetPath));
                }
            }

            return targetPath;
        }
        finally
        {
            await ToggleDisabled(domain, disabled);
        }
    }

    public async Task MarkRegistrationComplete(Guid firstRunToken)
    {
        var registration = GetByFirstRunToken(firstRunToken);
        registration.FirstRunToken = null;
        await this.SaveRegistrationInternal(registration);
    }

    public async Task<RegistrationStatus> GetRegistrationStatus(Guid firstRunToken)
    {
        var registration = GetByFirstRunToken(firstRunToken);

        if (null == registration)
        {
            return RegistrationStatus.Unknown;
        }

        //the other option here is to load the certs via the registry, which i dont like
        var svc = _systemHttpClient.CreateHttps<ICertificateStatusHttpClient>((OdinId)registration.PrimaryDomainName);
        try
        {
            var certsValidResponse = await svc.VerifyCertificatesValid();
            if (certsValidResponse.IsSuccessStatusCode)
            {
                if (certsValidResponse.Content)
                {
                    return RegistrationStatus.ReadyForPassword;
                }
                else
                {
                    return RegistrationStatus.AwaitingCertificate;
                }
            }
        }
        catch (System.Net.Http.HttpRequestException)
        {
            //hre.HResult == -2146232800
            return RegistrationStatus.AwaitingCertificate;
        }
        catch (Exception)
        {
            return RegistrationStatus.Unknown;
        }

        //TODO: Log system error here?

        return RegistrationStatus.Unknown;
    }

    private async Task SaveRegistrationInternal(IdentityRegistration registration)
    {
        string root = Path.Combine(RegistrationRoot, registration.Id.ToString());
        Directory.CreateDirectory(root);

        var json = OdinSystemSerializer.Serialize(registration);
        var regFilePath = GetRegFilePath(registration.Id);
        await File.WriteAllTextAsync(regFilePath, json);

        _logger.LogInformation("Write registration file for [{registrationId}]", registration.Id);

        Cache(registration);
    }

    public Task<PagedResult<IdentityRegistration>> GetList(PageOptions pageOptions = null)
    {
        var list = _cache.Values.ToList();
        return Task.FromResult(new PagedResult<IdentityRegistration>(PageOptions.All, 1, list));
    }

    public Task<IdentityRegistration> Get(string domain)
    {
        var reg = _trie.LookupExactName(domain);
        return Task.FromResult(reg);
    }

    public async Task<bool?> ToggleDisabled(string domain, bool disabled)
    {
        bool? result = null;
        var reg = _trie.LookupExactName(domain);
        if (reg != null)
        {
            result = reg.Disabled;
            if (reg.Disabled != disabled)
            {
                reg.Disabled = disabled;
                await SaveRegistrationInternal(reg);
            }
        }

        return result;
    }

    public async Task<UnixTimeUtc> MarkForDeletion(string domain)
    {
        var reg = _trie.LookupExactName(domain);
        if (reg == null)
        {
            throw new OdinClientException("Invalid domain");
        }

        var markedDate = UnixTimeUtc.Now();
        reg.MarkedForDeletionDate = UnixTimeUtc.Now();
        await SaveRegistrationInternal(reg);

        return markedDate.AddDays(_config.Registry.DaysUntilAccountDeletion);
    }

    public async Task UnmarkForDeletion(string domain)
    {
        var reg = _trie.LookupExactName(domain);
        if (reg == null)
        {
            throw new OdinClientException("Invalid domain");
        }

        reg.MarkedForDeletionDate = null;
        await SaveRegistrationInternal(reg);
    }

    private string GetRegFilePath(Guid registrationId)
    {
        return Path.Combine(RegistrationRoot, registrationId.ToString(), "reg.json");
    }

    private void LoadCache()
    {
        if (!Directory.Exists(RegistrationRoot))
        {
            return;
        }

        var directories = Directory.GetDirectories(RegistrationRoot);

        foreach (var dir in directories)
        {
            try
            {
                var path = Path.TrimEndingDirectorySeparator(dir.ToCharArray()).ToString();
                var potentialId = path.Split(Path.DirectorySeparatorChar).Last();
                if (!Guid.TryParse(potentialId, out var id))
                {
                    _logger.LogWarning(
                        "Identity Registry: Found invalid folder not in GUID format named [{potentialId}]; moving to next", potentialId);
                    continue;
                }

                var registration = LoadRegistration(id);
                if (registration == null)
                {
                    _logger.LogWarning("Identity Registry: could not find reg file for Id: [{id}]; moving to next", id.ToString());
                    continue;
                }

                _logger.LogInformation("Loaded Identity {identity}", registration.PrimaryDomainName);
                Cache(registration);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Identity Registry: Failed to load identity at path {dir}", dir);
            }
        }
    }

    private IdentityRegistration LoadRegistration(Guid id)
    {
        var regFile = GetRegFilePath(id);

        if (!File.Exists(regFile))
        {
            return null;
        }

        var json = File.ReadAllText(regFile);

        var registration = OdinSystemSerializer.Deserialize<IdentityRegistration>(json);
        return registration;
    }

    private void Cache(IdentityRegistration registration)
    {
        RegisterDotYouHttpClient(registration);

        _trie.TryRemoveDomain(registration.PrimaryDomainName);
        _trie.AddDomain(registration.PrimaryDomainName, registration);
        _cache[registration.Id] = registration;
    }

    private void UnloadRegistration(IdentityRegistration registration)
    {
        _cache.TryRemove(registration.Id, out _);
        _tenantContainer.Container().RemoveTenantScope(registration.PrimaryDomainName);
    }

    private IdentityRegistration GetByFirstRunToken(Guid firstRunToken)
    {
        var registration = _cache.Values.SingleOrDefault(reg => reg.FirstRunToken == firstRunToken);
        if (null == registration)
        {
            throw new OdinClientException("Invalid first run token", OdinClientErrorCode.UnknownId);
        }

        return registration;
    }

    private async Task InitializeCertificate(string domain)
    {
        var httpClient = _httpClientFactory.CreateClient(nameof(RegisterCertificateInitializerHttpClient));
        var uri = $"https://{_config.HttpsHostAndPort(domain)}/.well-known/acme-challenge/ping";
        try
        {
            await httpClient.GetAsync(uri);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("InitializeCertificate took too long to complete and the http request was cancelled");
        }
        catch (HttpRequestException e)
        {
            // This can happen if a new identity gets created, but the DNS server the backed uses does not yet
            // know the domain
            _logger.LogWarning("InitializeCertificate: {error}. Will retry on next request to the domain.", e.Message);
        }
    }

    private void RegisterCertificateInitializerHttpClient()
    {
        _httpClientFactory.Register(nameof(RegisterCertificateInitializerHttpClient), builder => builder
            .ConfigureHttpClient(c =>
            {
                // this is called everytime you request a httpclient
                c.Timeout = TimeSpan.FromMinutes(20);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false, // DO NOT CHANGE!
                };

                // Make sure we accept certifactes from letsencrypt staging servers if not in production
                if (!_useCertificateAuthorityProductionServers)
                {
                    handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
                }

                return handler;
            }));
    }

    private void RegisterDotYouHttpClient(IdentityRegistration idReg)
    {
        var tenantContext = this.CreateTenantContext(idReg);
        var domain = idReg.PrimaryDomainName;
        var sslRoot = tenantContext.SslRoot;
        var httpClientKey = OdinHttpClientFactory.HttpFactoryKey(domain);

        // SEB:NOTE
        // Below is the reason that we have to use IHttpClientFactory from HttpClientFactoryLite instead of the
        // baked-in one. We have to be able to create HttpClientHandlers on the fly, this is not possible with
        // the original IHttpClientFactory.
        _httpClientFactory.Register(httpClientKey, builder => builder.ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new HttpClientHandler
            {
                UseCookies = false, // DO NOT CHANGE!
                AllowAutoRedirect = false,
                SslProtocols = SslProtocols.None, //allow OS to choose;
            };

            var tc = _certificateServiceFactory.Create(sslRoot);
            var x509 = tc.GetSslCertificate(domain);
            if (x509 != null)
            {
                handler.ClientCertificates.Add(x509);
            }
            else
            {
                _logger.LogError("RegisterHttpClient: could not find certificate for {domain}", domain);
            }

            return handler;
        }).SetHandlerLifetime(TimeSpan.FromMinutes(2)));
    }

    //

    private Task DeletePayloads(IdentityRegistration identity)
    {
        return Task.Run(() =>
        {
            var shards = Directory.GetDirectories(ShardablePayloadRoot);
            foreach (var shard in shards)
            {
                var id = identity.Id.ToString();

                // Sanity
                if (string.IsNullOrEmpty(shard) || string.IsNullOrEmpty(id))
                {
                    throw new OdinSystemException("I just stopped you in wiping the wrong stuff!");
                }

                _logger.LogInformation("Deleting shard {shard} on {domain}", shard, identity.PrimaryDomainName);

                var payloadPath = Path.Combine(shard, id);
                if (Directory.Exists(payloadPath))
                {
                    try
                    {
                        Directory.Delete(payloadPath, true);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error deleting payload in '{path}': {error}", payloadPath, e.Message);
                    }
                }
            }
        });
    }

    //
}