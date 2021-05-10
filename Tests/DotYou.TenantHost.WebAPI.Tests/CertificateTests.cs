using NUnit.Framework;
using System;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using DotYou.Kernel.Services.Identity;

namespace DotYou.TenantHost.WebAPI.Tests
{
    public class CertificateTests
    {
        private TestScaffold scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            scaffold = new TestScaffold(folder);
            scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            scaffold.RunAfterAnyTests();
        }
        
        [Test]
        public void CanMakePublicKeyCertificatePortable()
        {
            var samContext = scaffold.Registry.ResolveContext(scaffold.Samwise);

            string certificatePath = samContext.TenantCertificate.Location.CertificatePath;

            string portableFormat;
            string key1Thumprint;
            string key1FriendlyName;
            string key1Subject;
            using (X509Certificate2 publicKey = new X509Certificate2(certificatePath))
            {
                var bytes = publicKey.Export(X509ContentType.Pkcs12);
                portableFormat = Convert.ToBase64String(bytes);
                key1Thumprint = publicKey.Thumbprint;
                key1FriendlyName = publicKey.FriendlyName;
                key1Subject = publicKey.Subject; 
                
                Console.WriteLine($"key 1 thumbprint: {key1Thumprint}");
                Console.WriteLine($"key 1 friendly name: {key1FriendlyName}");
                Console.WriteLine($"key 1 subject: {key1Subject}");
                //Console.WriteLine($"Portable: {portableFormat}");
            }

            new DomainCertificate(portableFormat);
            using (X509Certificate2 importedCert = new X509Certificate2(Convert.FromBase64String(portableFormat)))
            {
                //reload portable format
                //Console.WriteLine($"Portable: {publicKey.GetNameInfo(X509NameType.SimpleName, false)}");

                Console.WriteLine($"key 2 thumbprint: {importedCert.Thumbprint}");
                Console.WriteLine($"key 2 friendly name: {importedCert.FriendlyName}");
                Console.WriteLine($"key 2 subject: {importedCert.Subject}");

                Assert.IsTrue(key1Thumprint == importedCert.Thumbprint, "Thumbprints do not match");
                Assert.IsTrue(key1FriendlyName == importedCert.FriendlyName, "Friendly names do not match");
                Assert.IsTrue(key1Subject == importedCert.Subject, "Subjects do not match");
            }
            

        }
    }
}