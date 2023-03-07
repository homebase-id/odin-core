using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Autofac.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Refit;
using Serilog;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Certificate.Renewal;
using Youverse.Core.Trie;

namespace Youverse.Core.Services.Registry;

/// <summary>
/// Reads identities from the file system using a convention
/// </summary>
public class FileSystemIdentityRegistry : IIdentityRegistry
{
    private readonly Dictionary<Guid, IdentityRegistration> _cache;
    private readonly Trie.Trie<IdentityRegistration> _trie;
    private readonly string _tenantDataRootPath;
    private readonly CertificateRenewalConfig _certificateRenewalConfig;
    
    public FileSystemIdentityRegistry(string tenantDataRootPath, CertificateRenewalConfig certificateRenewalConfig)
    {
        if (!Directory.Exists(tenantDataRootPath))
        {
            throw new InvalidDataException($"Could find or access path at [{tenantDataRootPath}]");
        }
        
        _cache = new Dictionary<Guid, IdentityRegistration>();
        _trie = new Trie<IdentityRegistration>();
        _tenantDataRootPath = tenantDataRootPath;
        _certificateRenewalConfig = certificateRenewalConfig;
    }

    public void Initialize()
    {
        LoadCache();
    }

    public Guid ResolveId(string domain)
    {
        var reg = _trie.LookupName(domain);
        return reg.Id;
    }

    public Task<bool> IsIdentityRegistered(string domain)
    {
        return Task.FromResult(_trie.LookupName(domain) != null);
    }

    public async Task<Guid> AddRegistration(IdentityRegistrationRequest request)
    {
        var registration = new IdentityRegistration()
        {
            Id = Guid.NewGuid(),
            PrimaryDomainName = request.OdinId,
            IsCertificateManaged = request.IsCertificateManaged,
            FirstRunToken = Guid.NewGuid()
        };

        await this.SaveRegistrationInternal(registration);

        if (request.OptionalCertificatePemContent == null)
        {
            await this.InitializeCertificate(request.OdinId);
            // var ctx = TenantContext.Create(registration.Id, request.OdinId, _tenantDataRootPath, _certificateRenewalConfig);
            // ITenantCertificateService tenantCertificateService = new TenantCertificateService(ctx);
            // var logger = new NullLoggerFactory().CreateLogger<LetsEncryptTenantCertificateRenewalService>()
            // ITenantCertificateRenewalService renewalService = new LetsEncryptTenantCertificateRenewalService(logger, ctx, tenantCertificateService, , ctx.)
        }
        else
        {
            //optionally, let an ssl certificate be provided 
            //TODO: is there a way to pull a specific tenant's service config from Autofac?
            ITenantCertificateService tc = new TenantCertificateService(TenantContext.Create(registration.Id, request.OdinId, _tenantDataRootPath, _certificateRenewalConfig));
            await tc.SaveSslCertificate(registration.Id, request.OdinId.DomainName, request.OptionalCertificatePemContent);
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
        var svc = SystemHttpClient.CreateHttps<ICertificateStatusHttpClient>((OdinId)registration.PrimaryDomainName);
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
        catch (HttpRequestException hre)
        {
            //hre.HResult == -2146232800
            return RegistrationStatus.AwaitingCertificate;
        }
        catch (Exception e)
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

        var json = DotYouSystemSerializer.Serialize(registration);
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
        var reg = _trie.LookupName(domain);
        return Task.FromResult(reg);
    }

    public async Task EnsureCertificate(string domain)
    {
        try
        {
            var svc = SystemHttpClient.CreateHttps<ICertificateStatusHttpClient>((OdinId)domain);
            var response = await svc.EnsureValidCertificates();
            await response.EnsureSuccessStatusCodeAsync();
        }
        catch (ApiException e)
        {
            //TODO: need to log an error here and notify sys admins?
            // keep a list of those continuing to fail so we can deactivate, etc.
            Log.Error($"{e.RequestMessage.Method} to {e.RequestMessage.RequestUri} failed with {e.ReasonPhrase}.\n Exception Message: [{e.Message}]");
        }
        catch (HttpRequestException ex)
        {
            //TODO: need to log an error here and notify sys admins?
            // keep a list of those continuing to fail so we can deactivate, etc.
            Log.Error($"Request to EnsureValidCertificates failed with {ex.StatusCode}.\n Exception Message: [{ex.Message}]");
        }
    }

    public async Task EnsureCertificates()
    {
        //TODO: could optimize by breaking into multiple threaded requests, etc.
        var identities = await this.GetList(PageOptions.All);
        foreach (var ident in identities.Results)
        {
            await this.EnsureCertificate(ident.PrimaryDomainName);
        }
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

                var registration = DotYouSystemSerializer.Deserialize<IdentityRegistration>(json);

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
        if (null != _trie.LookupName(registration.PrimaryDomainName))
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
            throw new YouverseClientException("Invalid first run token", YouverseClientErrorCode.UnknownId);
        }

        return registration;
    }

    private async Task InitializeCertificate(string domain)
    {
        try
        {
            var svc = SystemHttpClient.CreateHttp<ICertificateStatusHttpClient>((OdinId)domain);
            var response = await svc.InitializeCertificate();
            await response.EnsureSuccessStatusCodeAsync();
        }
        catch (ApiException e)
        {
            //TODO: need to log an error here and notify sys admins?
            // keep a list of those continuing to fail so we can deactivate, etc.
            Log.Error($"{e.RequestMessage.Method} to {e.RequestMessage.RequestUri} failed with {e.ReasonPhrase}.\n Exception Message: [{e.Message}]");
        }
        catch (HttpRequestException ex)
        {
            //TODO: need to log an error here and notify sys admins?
            // keep a list of those continuing to fail so we can deactivate, etc.
            Log.Error($"Request to InitializeCertificate failed with {ex.StatusCode}.\n Exception Message: [{ex.Message}]");
        }
    }
}