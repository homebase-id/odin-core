using System;
using System.Security.Cryptography.X509Certificates;
using Dawn;
using DotYou.Types;

namespace DotYou.Kernel.Services.Identity
{
    public class DomainCertificate : IDomainCertificate
    {
        private readonly string _certData;

        /// <summary>
        /// A base64 string of the certificate data
        /// </summary>
        /// <param name="publicKeyCertificate"></param>
        public DomainCertificate(string publicKeyCertificate)
        {
            Guard.Argument(publicKeyCertificate, nameof(publicKeyCertificate)).NotEmpty();

            _certData = publicKeyCertificate;
            ParseCertificate();
        }

        public DotYouIdentity DotYouId { get; set; }

        private void ParseCertificate()
        {
            using var cert = new X509Certificate2(Convert.FromBase64String(_certData));
            this.DotYouId = (DotYouIdentity) CertificateUtils.GetDomainFromCommonName(cert.Subject);
        }
    }
}