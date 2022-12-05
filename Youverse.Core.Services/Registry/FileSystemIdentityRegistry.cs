using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Trie;

namespace Youverse.Core.Services.Registry;

/// <summary>
/// Reads identities from the file system using a convention
/// </summary>
public class FileSystemIdentityRegistry : IIdentityRegistry
{
    private readonly Trie.Trie<IdentityRegistration> _trie;
    private readonly string _tenantDataRootPath;
    private readonly string _tempDataStoragePath;

    public FileSystemIdentityRegistry(string tenantDataRootPath, string tempDataStoragePath)
    {
        if (!Directory.Exists(tenantDataRootPath))
        {
            throw new InvalidDataException($"Could find or access path at [{tenantDataRootPath}]");
        }

        if (!Directory.Exists(tempDataStoragePath))
        {
            throw new InvalidDataException($"Could find or access path at [{tempDataStoragePath}]");
        }

        _trie = new Trie<IdentityRegistration>();
        _tenantDataRootPath = tenantDataRootPath;
        _tempDataStoragePath = tempDataStoragePath;
    }


    public void Initialize()
    {
        var dirs = Directory.GetDirectories(_tenantDataRootPath, "*", SearchOption.TopDirectoryOnly);

        foreach (var d in dirs)
        {
            var id = d.Split(Path.DirectorySeparatorChar).LastOrDefault();
            if (Guid.TryParse(id, out var g))
            {
                var regFile = GetRegFilePath(g);
                var json = File.ReadAllText(regFile);
                var registration = DotYouSystemSerializer.Deserialize<IdentityRegistration>(json);
                _trie.AddDomain(registration.DomainName, registration);
            }
        }
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

    public Task<bool> HasValidCertificate(string domain)
    {
        var reg = _trie.LookupName(domain);
        if (null == reg)
        {
            return Task.FromResult(false);
        }

        var cert = CertificateResolver.LoadCertificate(reg.DomainName, this.GetCertificatePath(reg), this.GetPrivateKeyPath(reg));

        if (cert == null)
        {
            return Task.FromResult(false);
        }

        var now = DateTime.Now;
        var isValid = now < cert.NotAfter && now > cert.NotBefore;

        return Task.FromResult(isValid);
    }

    public async Task AddRegistration(IdentityRegistrationRequest request)
    {
        var registration = new IdentityRegistration()
        {
            Id = Guid.NewGuid(),
            DomainName = request.DotYouId,
            IsCertificateManaged = request.IsCertificateManaged,
            CertificateRenewalInfo = new CertificateRenewalInfo()
            {
                CreatedTimestamp = UnixTimeUtc.Now(),
                CertificateSigningRequest = request.CertificateSigningRequest
            },
        };

        string root = Path.Combine(_tenantDataRootPath, registration.Id.ToString());
        Console.WriteLine($"Writing certificates to path [{root}]");

        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
        }

        var json = DotYouSystemSerializer.Serialize(registration);
        await File.WriteAllTextAsync(GetRegFilePath(registration.Id), json);

        await File.WriteAllTextAsync(GetCertificatePath(registration), request.CertificateContent.PublicKeyCertificate);
        await File.WriteAllTextAsync(GetPrivateKeyPath(registration), request.CertificateContent.PrivateKey);
        // await File.WriteAllTextAsync(registration.FullChainCertificateRelativePath, request.CertificateContent.FullChain);
    }

    private string GetRegFilePath(Guid registrationId)
    {
        return Path.Combine(_tempDataStoragePath, registrationId.ToString(), "reg.json");
    }

    public Task<PagedResult<IdentityRegistration>> GetList(PageOptions pageOptions)
    {
        var domains = Directory.GetDirectories(_tenantDataRootPath);

        //TODO: get from DI
        foreach (var domain in domains)
        {
            //todo: add isValidDomain name check

            // Guid domainId = CalculateDomainId(dotYouId);
            // string certificatePath = PathUtil.Combine(rootPath, registryId.ToString(), "ssl", domainId.ToString(), "certificate.crt");
            // string privateKeyPath = PathUtil.Combine(rootPath, registryId.ToString(), "ssl", domainId.ToString(), "private.key");
            // return LoadCertificate(certificatePath, privateKeyPath);

            DotYouIdentity dotYouId = (DotYouIdentity)domain;
            Guid domainId = CertificateResolver.CalculateDomainId(dotYouId);
            var certificate = CertificateResolver.GetSslCertificate(_tenantDataRootPath, domainId, dotYouId);
        }

        throw new NotImplementedException();
    }

    public Task<IdentityRegistration> Get(string domainName)
    {
        throw new NotImplementedException();
    }

    private string GetCertificatePath(IdentityRegistration registration)
    {
        string path = Path.Combine(_tenantDataRootPath, registration.Id.ToString(), "ssl", registration.DomainName.ToLower(), "certificate.crt");
        return path;
    }

    private string GetPrivateKeyPath(IdentityRegistration registration)
    {
        string path = Path.Combine(_tenantDataRootPath, registration.Id.ToString(), "ssl", registration.DomainName.ToLower(), "private.key");
        return path;
    }
}