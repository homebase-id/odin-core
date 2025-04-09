using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Time;
using Odin.Services.Configuration.Eula;

namespace Odin.Hosting.Tests.OwnerApi.Configuration.SystemInit
{
    public class EulaTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(initializeIdentity: false);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [SetUp]
        public void Setup()
        {
            _scaffold.ClearAssertLogEventsAction();
            _scaffold.ClearLogEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _scaffold.AssertLogEvents();
        }


        [Test]
        public async Task CanSignAndGetSignatureHistory()
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

            var eulaResponse = await ownerClient.Configuration.IsEulaSignatureRequired();
            ClassicAssert.IsTrue(eulaResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(eulaResponse.Content);

            const string version = EulaSystemInfo.RequiredVersion;
            var signature = Guid.NewGuid().ToByteArray();
            await ownerClient.Configuration.MarkEulaSigned(new MarkEulaSignedRequest()
            {
                Version = version,
                SignatureBytes = signature
            });

            var eulaResponse2 = await ownerClient.Configuration.IsEulaSignatureRequired();
            ClassicAssert.IsTrue(eulaResponse2.IsSuccessStatusCode);
            ClassicAssert.IsFalse(eulaResponse2.Content);


            var getHistoryResponse = await ownerClient.Configuration.GetEulaSignatureHistory();
            ClassicAssert.IsTrue(getHistoryResponse.IsSuccessStatusCode);

            var history = getHistoryResponse.Content;
            ClassicAssert.IsNotNull(history);
            var eulaSignature = history.SingleOrDefault(s => s.Version == EulaSystemInfo.RequiredVersion);
            ClassicAssert.IsNotNull(eulaSignature);
            
            ClassicAssert.IsTrue(eulaSignature.SignatureBytes.Length == signature.Length);

            var nowMs = UnixTimeUtc.Now();
            ClassicAssert.IsTrue(eulaSignature.SignatureDate < nowMs);
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

            ClassicAssert.IsTrue(markSignedResponse.StatusCode == HttpStatusCode.BadRequest);
        }
    }
}