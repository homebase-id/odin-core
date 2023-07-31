using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Odin.Core.Services.Certificate;
using Odin.Core.Services.Registry.Registration;
using Odin.Core.Trie;
using Serilog;
using IHttpClientFactory = HttpClientFactoryLite.IHttpClientFactory;

namespace Odin.Core.Services.Registry;

/// <summary>
/// Reads identities from the file system using a convention
/// </summary>
public class FileSystemIdentityRegistry : IIdentityRegistry
{
    private readonly ILogger<FileSystemIdentityRegistry> _logger;
    private readonly Dictionary<Guid, IdentityRegistration> _cache;
    private readonly Trie<IdentityRegistration> _trie;
    private readonly ICertificateServiceFactory _certificateServiceFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISystemHttpClient _systemHttpClient;
    private readonly bool _useCertificateAuthorityProductionServers;
    private readonly string _tenantDataRootPath;
    private readonly string _tenantDataPayloadPath;
   
    public FileSystemIdentityRegistry(
        ILogger<FileSystemIdentityRegistry> logger,
        ICertificateServiceFactory certificateServiceFactory,
        IHttpClientFactory httpClientFactory,
        ISystemHttpClient systemHttpClient,
        bool useCertificateAuthorityProductionServers,
        string tenantDataRootPath, 
        string tenantDataPayloadPath)
    {
        if (!Directory.Exists(tenantDataRootPath))
        {
            throw new InvalidDataException($"Could find or access path at [{tenantDataRootPath}]");
        }

        _cache = new Dictionary<Guid, IdentityRegistration>();
        _trie = new Trie<IdentityRegistration>();
        _logger = logger;
        _certificateServiceFactory = certificateServiceFactory;
        _httpClientFactory = httpClientFactory;
        _systemHttpClient = systemHttpClient;
        _useCertificateAuthorityProductionServers = useCertificateAuthorityProductionServers;
        _tenantDataRootPath = tenantDataRootPath;
        _tenantDataPayloadPath = tenantDataPayloadPath;

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
    
    public Task<bool> IsIdentityRegistered(string domain)
    {
        return Task.FromResult(_trie.LookupExactName(domain) != null);
    }

    public async Task<Guid> AddRegistration(IdentityRegistrationRequest request)
    {
        var registration = new IdentityRegistration()
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PrimaryDomainName = request.OdinId,
            IsCertificateManaged = request.IsCertificateManaged,
            FirstRunToken = Guid.NewGuid()
        };

        await this.SaveRegistrationInternal(registration);

        if (request.OptionalCertificatePemContent == null)
        {
            await this.InitializeCertificate(request.OdinId);
        }
        else
        {
            //optionally, let an ssl certificate be provided 
            //TODO: is there a way to pull a specific tenant's service config from Autofac?
            var tenantContext = TenantContext.Create(registration.Id, request.OdinId, _tenantDataRootPath, _tenantDataPayloadPath);
            
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
            string tenantRoot = Path.Combine(_tenantDataRootPath, registration.Id.ToString());
            Directory.Delete(tenantRoot, true);
            _trie.RemoveDomain(domain);
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
        string root = Path.Combine(_tenantDataRootPath, registration.Id.ToString());
        Directory.CreateDirectory(root);

        var json = OdinSystemSerializer.Serialize(registration);
        await File.WriteAllTextAsync(GetRegFilePath(registration.Id), json);

        Log.Information($"Write registration file for [{registration.Id}]");

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

    private string GetRegFilePath(Guid registrationId)
    {
        return Path.Combine(_tenantDataRootPath, registrationId.ToString(), "reg.json");
    }

    private void LoadCache()
    {
        var directories = Directory.GetDirectories(_tenantDataRootPath);

        foreach (var dir in directories)
        {
            try
            {
                var path = Path.TrimEndingDirectorySeparator(dir.ToCharArray()).ToString();
                var potentialId = path.Split(Path.DirectorySeparatorChar).Last();
                if (!Guid.TryParse(potentialId, out var id))
                {
                    Log.Warning($"Identity Registry: Found invalid folder not in GUID format named [{potentialId}]; moving to next");
                    continue;
                }

                var regFile = GetRegFilePath(id);

                if (!File.Exists(regFile))
                {
                    Log.Warning($"Identity Registry: could not find reg file for Id: [{id.ToString()}]; moving to next");
                    continue;
                }

                var json = File.ReadAllText(regFile);

                var registration = OdinSystemSerializer.Deserialize<IdentityRegistration>(json);

                Cache(registration);
            }
            catch (Exception e)
            {
                //TODO: log as startup error with details.
                string message = $"Identity Registry: Failed to load identity at path [{dir}]";
                Log.Error(e, message);
                continue;
            }
        }
    }

    private void Cache(IdentityRegistration registration)
    {
        RegisterDotYouHttpClient(registration);
        
        if (null != _trie.LookupExactName(registration.PrimaryDomainName))
        {
            _trie.RemoveDomain(registration.PrimaryDomainName);
        }

        _trie.AddDomain(registration.PrimaryDomainName, registration);

        if (_cache.ContainsKey(registration.Id))
        {
            _cache.Remove(registration.Id);
        }
        
        _cache.Add(registration.Id, registration);        
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
        var uri = $"https://{domain}/.well-known/acme-challenge/ping";
        try
        {
            await httpClient.GetAsync(uri);
        }
        catch (TaskCanceledException)
        {
            Log.Warning("InitializeCertificate took too long to complete and the http request was cancelled");
        }
    }

    private void RegisterCertificateInitializerHttpClient()
    {
        _httpClientFactory.Register(nameof(RegisterCertificateInitializerHttpClient), builder => builder
            .ConfigureHttpClient(c =>
            {
                // this is called everytime you request a httpclient
                c.Timeout = TimeSpan.FromMinutes(5);
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
        var tenantContext = TenantContext.Create(
            idReg.Id, 
            idReg.PrimaryDomainName, 
            _tenantDataRootPath, 
            _tenantDataPayloadPath, 
            false);

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
}
