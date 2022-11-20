using System;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Registry;

/// <summary>
/// Reads identities from the file system using a convention
/// </summary>
public class FileSystemIdentityRegistry : IIdentityRegistry
{
    private readonly string _dataRoot;

    public FileSystemIdentityRegistry(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    public void Initialize()
    {
        //read from disk; load in trie
    }

    public Guid ResolveId(string domainName)
    {
        throw new NotImplementedException();
    }
    
    public Task<bool> IsIdentityRegistered(string domain)
    {
        throw new NotImplementedException();
    }

    public Task Add(IdentityRegistrationRequest request)
    {
        /*
         * By the time this method is called, the certificates already exist
         * so this needs to accept the certificate
         * th question is
         *
         * what calls this method?
         * when is it called?
         *
         * well, there is a provisioning site that is managing the process of creating a wildcard domain
         * but what about creating it for a personal domain?
         */
        var registration = new IdentityRegistration()
        {
            Id = Guid.NewGuid(),
            DomainName = default,
            CertificateRenewalInfo = null,
            IsCertificateManaged = default,
            PrivateKeyRelativePath = default,
            FullChainCertificateRelativePath = default,
            PublicKeyCertificateRelativePath = default
        };
        
        throw new NotImplementedException();


    }

    public Task<PagedResult<IdentityRegistration>> GetList(PageOptions pageOptions)
    {
        var domains = Directory.GetDirectories(_dataRoot);

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
            var certificate = CertificateResolver.GetSslCertificate(_dataRoot, domainId, dotYouId);
        }
        throw new NotImplementedException();

    }

    public Task<IdentityRegistration> Get(string domainName)
    {
        throw new NotImplementedException();
    }
    
    private string GetCertificatePath(DotYouIdentity dotYouId)
    {
        throw new NotImplementedException();

        // string certificatePath = PathUtil.Combine(_dataRoot, registryId.ToString(), "ssl", domainId.ToString(), "certificate.crt");
        // string privateKeyPath = PathUtil.Combine(rootPath, registryId.ToString(), "ssl", domainId.ToString(), "private.key");
    }
    
}