using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Dawn;
using Youverse.Core.Services.Registry;

namespace Youverse.Core.Services.Identity
{
    /// <summary>
    /// An IdentityCertificate defines a certificate held by an individual human or organization.
    /// </summary>
    public sealed class IdentityCertificate
    {
        //private empty ctor handles deserialization
        private IdentityCertificate() { }

        public IdentityCertificate(Guid key, string domain)
        {
            Guard.Argument(key, nameof(key)).NotEqual(Guid.Empty);
            Guard.Argument(domain, nameof(domain)).NotEmpty();
            
            Key = key;
            DomainName = domain;
        }

        public Guid Key
        {
            get;
        }

        public string DomainName { get; }
        
    }
}