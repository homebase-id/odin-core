using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Configuration;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Membership.Circles;

namespace Odin.Hosting.Tests.OwnerApi.Configuration.SystemInit
{
    public class EulaTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(initializeIdentity: false);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task CanSignEula()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

            var eulaResponse = await ownerClient.Configuration.IsEulaAgreementRequired();
            Assert.IsTrue(eulaResponse.IsSuccessStatusCode);
            Assert.IsTrue(eulaResponse.Content);

            const string version = "1234";
            await ownerClient.Configuration.MarkEulaSigned(new MarkEulaSignedRequest()
            {
                Version = version,
                SignatureBytes = Guid.NewGuid().ToByteArray()
            });
            
            var eulaResponse2 = await ownerClient.Configuration.IsEulaAgreementRequired();
            Assert.IsTrue(eulaResponse2.IsSuccessStatusCode);
            Assert.IsFalse(eulaResponse2.Content);
        }

        [Test]
        public void FailOwnerLogin_Without_Eula_Signed()
        {
            Assert.Inconclusive("TODO");
        }
        
        [Test]
        public void FailToRenderHome_Without_Eula_Signed()
        {
            Assert.Inconclusive("TODO");
        }
        
    }
}