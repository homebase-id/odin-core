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
        
    }
}