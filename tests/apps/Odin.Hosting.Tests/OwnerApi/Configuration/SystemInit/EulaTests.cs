using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Configuration.Eula;

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
        public async Task CanSignAndGetSignatureHistory()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

            var eulaResponse = await ownerClient.Configuration.IsEulaSignatureRequired();
            Assert.IsTrue(eulaResponse.IsSuccessStatusCode);
            Assert.IsTrue(eulaResponse.Content);

            const string version = EulaSystemInfo.RequiredVersion;
            var signature = Guid.NewGuid().ToByteArray();
            await ownerClient.Configuration.MarkEulaSigned(new MarkEulaSignedRequest()
            {
                Version = version,
                SignatureBytes = signature
            });

            var eulaResponse2 = await ownerClient.Configuration.IsEulaSignatureRequired();
            Assert.IsTrue(eulaResponse2.IsSuccessStatusCode);
            Assert.IsFalse(eulaResponse2.Content);


            var getHistoryResponse = await ownerClient.Configuration.GetEulaSignatureHistory();
            Assert.IsTrue(getHistoryResponse.IsSuccessStatusCode);

            var history = getHistoryResponse.Content;
            Assert.IsNotNull(history);
            var eulaSignature = history.SingleOrDefault(s => s.Version == EulaSystemInfo.RequiredVersion);
            Assert.IsNotNull(eulaSignature);
            
            Assert.IsTrue(eulaSignature.SignatureBytes.Length == signature.Length);

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Assert.IsTrue(eulaSignature.SignatureDate < nowMs);
        }

        [Test]
        public async Task FailToSignInvalidEulaVersion()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

            string version = Guid.NewGuid().ToString("N");
            var markSignedResponse = await ownerClient.Configuration.MarkEulaSigned(new MarkEulaSignedRequest()
            {
                Version = version,
                SignatureBytes = Guid.NewGuid().ToByteArray()
            });

            Assert.IsTrue(markSignedResponse.StatusCode == HttpStatusCode.BadRequest);
        }
    }
}