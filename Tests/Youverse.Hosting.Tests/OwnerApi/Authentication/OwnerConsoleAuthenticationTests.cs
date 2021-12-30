using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Services.Base;

namespace Youverse.Hosting.Tests.OwnerApi.Authentication
{
    public class OwnerConsoleAuthenticationTests
    {
        private TestScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new TestScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task CanForceNewPasswordAtProvisioning()
        {
            const string password = "EnSøienØ$";
            await _scaffold.ForceNewPassword(DotYouIdentities.Frodo, password);

            var authResult = await _scaffold.LoginToOwnerConsole(DotYouIdentities.Frodo, password);
            using var client = _scaffold.CreateOwnerApiHttpClient(DotYouIdentities.Frodo, authResult);
            var svc = RestService.For<IOwnerAuthenticationClient>(client);
            var isValidResponse = await svc.IsValid(authResult.SessionToken);
            Assert.IsTrue(isValidResponse.IsSuccessStatusCode);
            Assert.IsTrue(isValidResponse.Content);
        }

        [Test]
        public async Task CanLogInAndOutOfOwnerConsole()
        {
            const string password = "EnSøienØ$";
            await _scaffold.ForceNewPassword(DotYouIdentities.Frodo, password);

            var authResult = await _scaffold.LoginToOwnerConsole(DotYouIdentities.Frodo, password);
            using var client = _scaffold.CreateOwnerApiHttpClient(DotYouIdentities.Frodo, authResult);
            
            var svc = RestService.For<IOwnerAuthenticationClient>(client);
            var isValidResponse = await svc.IsValid(authResult.SessionToken);
            Assert.IsTrue(isValidResponse.IsSuccessStatusCode);
            Assert.IsTrue(isValidResponse.Content);

            await svc.Expire(authResult.SessionToken);
                
            var isValidResponse2 = await svc.IsValid(authResult.SessionToken);
            Assert.IsTrue(isValidResponse2.IsSuccessStatusCode);
            Assert.IsFalse(isValidResponse2.Content);
        }
        
        [Test]
        public async Task FailsWithoutDeviceUid()
        {
            const string password = "EnSøienØ$";
            await _scaffold.ForceNewPassword(DotYouIdentities.Frodo, password);

            var authResult = await _scaffold.LoginToOwnerConsole(DotYouIdentities.Frodo, password);
            using var client = _scaffold.CreateOwnerApiHttpClient(DotYouIdentities.Frodo, authResult);
            client.DefaultRequestHeaders.Remove(DotYouHeaderNames.DeviceUid);
            
            var svc = RestService.For<IOwnerAuthenticationClient>(client);
            var isValidResponse = await svc.IsValid(authResult.SessionToken);
            Assert.IsTrue(isValidResponse.IsSuccessStatusCode);
            Assert.IsTrue(isValidResponse.Content);
        }

        //[Test]
        // public Task CannotUseAppLoginCookieToAccessOwnerConsole()
        // {
        //     Assert.Inconclusive("TODO - need to build app login first");
        //     return Task.CompletedTask;
        // }
    }
}