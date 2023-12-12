using System;
using System.Collections;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Time;
using Odin.Hosting.Controllers.OwnerToken.Auth;
using Odin.Hosting.Tests.OwnerApi.Authentication;
using Refit;

namespace Odin.Hosting.Tests._Universal.Owner.AccountManagement
{
    public class DeleteAccountTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(false, false);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }
        
        public static IEnumerable TestCases()
        {
            // yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
            // yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.NotFound };
            yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task CanMarkAccountForDeletion(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
        {
            var identity = TestIdentities.TomBombadil;
            const string password = "8833CC039d!!~!";
            await _scaffold.OldOwnerApi.SetupOwnerAccount(identity.OdinId, true, password);

            var ownerClient = _scaffold.CreateOwnerApiClientRedux(identity);

            // Must be logged in
            var loginResult = await this.Login(identity.OdinId, password);
            Assert.IsTrue(loginResult.IsSuccessStatusCode);
            
            // var response = await ownerClient.AccountManagement.DeleteAccount();

        }

        private async Task<ApiResponse<OwnerAuthenticationResult>> Login(string identity, string password)
        {
            using HttpClient authClient = new()
            {
                BaseAddress = new Uri($"https://{identity}")
            };

            var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

            var nonceResponse = await svc.GenerateAuthenticationNonce();
            Assert.IsTrue(nonceResponse.IsSuccessStatusCode, "server failed when getting nonce");
            var clientNonce = nonceResponse.Content;

            var nonce = new NonceData(clientNonce!.SaltPassword64, clientNonce.SaltKek64, clientNonce.PublicPem, clientNonce.CRC)
            {
                Nonce64 = clientNonce.Nonce64
            };

            var reply = PasswordDataManager.CalculatePasswordReply(password, nonce);
            var response = await svc.Authenticate(reply);
            return response;
        }
    }
}