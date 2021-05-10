using DotYou.Types;
using NUnit.Framework;
using System.Reflection;
using Refit;

namespace DotYou.TenantHost.WebAPI.Tests
{
    public class AdminClientAuthenticationTests
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
        public void CanAuthenticateFrodoOnFrodosSite()
        {
            using var client = scaffold.CreateHttpClient(scaffold.Frodo);

            RestService.For<IAdminAuthenticationClient>(client);

        }

        public void FailToAuthenticateSamOnFrodosSite()
        {
            
        }
    }
}