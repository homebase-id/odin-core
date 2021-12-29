using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Hosting.Tests.OwnerApi;
using Youverse.Hosting.Tests.OwnerApi.Authentication;

namespace Youverse.Hosting.Tests.AppAPI.Authentication
{
    [TestFixture]
    public class AppAuthenticationTests
    {
        private OwnerConsoleTestScaffold _ownerTestScaffold;
        private AppTestScaffold _appTestScaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _ownerTestScaffold = new OwnerConsoleTestScaffold(folder);
            _ownerTestScaffold.RunBeforeAnyTests();

            _appTestScaffold = new AppTestScaffold(folder);
            _appTestScaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _ownerTestScaffold.RunAfterAnyTests();
            _appTestScaffold.RunBeforeAnyTests();
        }

        [Test]
        public async Task CanAuthenticateApp()
        {
            /*
                1. Create OwnerConsoleHttpClient that is logged in
                2. Use OwnerConsoleHttpClient to POST /api/owner/v1/auth/exchange
                3. Store session token and client 1/2 key in app cookie
                4. Now we can create App Http Client
             */

            using (var ownerClient = _ownerTestScaffold.CreateHttpClient(DotYouIdentities.Samwise))
            {
                var ownerAuth = RestService.For<IOwnerAuthenticationClient>(ownerClient);
                
            }
        }
    }
}