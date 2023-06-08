using System.Reflection;
using NUnit.Framework;

namespace Odin.Hosting.Tests.OwnerApi.Authentication
{
    public class OwnerConsoleAuthenticationTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test]
        [Ignore("Setup additional digital identities dedicated to testing these")]
        public void CanForceNewPasswordAtProvisioning()
        {
            // const string password = "EnSøienØ$";
            // await _scaffold.OwnerApi.ForceNewPassword(TestIdentities.Frodo, password);
            //
            // var (authResult, sharedSecret) = await _scaffold.OwnerApi.LoginToOwnerConsole(TestIdentities.Frodo, password);
            // var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo, authResult);
            // var svc = RestService.For<IOwnerAuthenticationClient>(client);
            // var isValidResponse = await svc.IsValid(authResult.Id);
            // Assert.IsTrue(isValidResponse.IsSuccessStatusCode);
            // Assert.IsTrue(isValidResponse.Content);
        }

        [Test]
        [Ignore("Setup additional digital identities dedicated to testing these")]
        public void CanLogInAndOutOfOwnerConsole()
        {
            // const string password = "EnSøienØ$";
            // await _scaffold.OwnerApi.ForceNewPassword(TestIdentities.Frodo, password);
            //
            // var (authResult, sharedSecret) = await _scaffold.OwnerApi.LoginToOwnerConsole(TestIdentities.Frodo, password);
            // var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo, authResult);
            //
            // var svc = RestService.For<IOwnerAuthenticationClient>(client);
            // var isValidResponse = await svc.IsValid(authResult.Id);
            // Assert.IsTrue(isValidResponse.IsSuccessStatusCode);
            // Assert.IsTrue(isValidResponse.Content);
            //
            // await svc.Expire(authResult.Id);
            //
            // var isValidResponse2 = await svc.IsValid(authResult.Id);
            // Assert.IsTrue(isValidResponse2.IsSuccessStatusCode);
            // Assert.IsFalse(isValidResponse2.Content);
        }

        [Test]
        [Ignore("Setup additional digital identities dedicated to testing these")]
        public void FailsWithoutDeviceUid()
        {
            // const string password = "EnSøienØ$";
            // await _scaffold.OwnerApi.ForceNewPassword(TestIdentities.Frodo, password);
            //
            // var (authResult, sharedSecret) = await _scaffold.OwnerApi.LoginToOwnerConsole(TestIdentities.Frodo, password);
            // client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo, authResult);
            // client.DefaultRequestHeaders.Remove(DotYouHeaderNames.DeviceUid);
            //
            // var svc = RestService.For<IOwnerAuthenticationClient>(client);
            // var isValidResponse = await svc.IsValid(authResult.Id);
            // Assert.IsTrue(isValidResponse.IsSuccessStatusCode);
            // Assert.IsTrue(isValidResponse.Content);
        }

        //[Test]
        // public Task CannotUseAppLoginCookieToAccessOwnerConsole()
        // {
        //     Assert.Inconclusive("TODO - need to build app login first");
        //     return Task.CompletedTask;
        // }
    }
}