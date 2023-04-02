using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Grpc.Core.Interceptors;
using NuGet.Frameworks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken;
using Youverse.Hosting.Tests.OwnerApi.Circle;

namespace Youverse.Hosting.Tests.AppAPI.Circle
{
    public class AppCorsTest
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

        [SetUp]
        public void Setup()
        {
            //runs before each test 
            //_scaffold.DeleteData(); 
        }

        [Test]
        public async Task CorsMiddleWareInjectsAppCorsHostNameForAnyAppPathWithOptionsVerb()
        {
            Guid appId = Guid.NewGuid();
            const string appCorsHostName = "app.domain.org";
            var appContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId,
                TestIdentities.Frodo, canReadConnections: true,
                appCorsHostName: appCorsHostName);

            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(appContext))
            {
                const string corsHeaderName = "Access-Control-Allow-Origin";
                var request = new HttpRequestMessage(HttpMethod.Options, $"/api/apps/v1/{Guid.NewGuid()}"); //any path should work
                var response = await client.SendAsync(request);

                Assert.IsTrue(response.Headers.TryGetValues(corsHeaderName, out var values), "could not find header");

                var value = values.SingleOrDefault();
                Assert.IsNotNull(value);
                Assert.IsTrue(value == $"https://{appCorsHostName}");

                Assert.IsTrue(response.IsSuccessStatusCode);
            }
        }

        [Test]
        public async Task CorsMiddleWareInjectsAppCorsHostNameForAnyAppPathWithGetVerb()
        {
            Guid appId = Guid.NewGuid();
            const string appCorsHostName = "app.domain.org";
            var appContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId,
                TestIdentities.Frodo,
                canReadConnections: true,
                appCorsHostName: appCorsHostName);

            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(appContext))
            {
                const string corsHeaderName = "Access-Control-Allow-Origin";
                var request = new HttpRequestMessage(HttpMethod.Get, "/api/apps/v1/auth/verifytoken");
                var response = await client.SendAsync(request);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Status code was {response.StatusCode}");

                Assert.IsTrue(response.Headers.TryGetValues(corsHeaderName, out var values), "could not find header");

                var value = values.SingleOrDefault();
                Assert.IsNotNull(value);
                Assert.IsTrue(value == $"https://{appCorsHostName}");

                Assert.IsTrue(response.IsSuccessStatusCode);
            }
        }

        [Test]
        public async Task CorsMiddleWareInjectsAppCorsHostNameForAnyAppPathWithPostVerb()
        {
            Guid appId = Guid.NewGuid();
            const string appCorsHostName = "app.domain.org";
            var appContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId,
                TestIdentities.Frodo,
                canReadConnections: true,
                appCorsHostName: appCorsHostName);

            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(appContext))
            {
                const string corsHeaderName = "Access-Control-Allow-Origin";
                var request = new HttpRequestMessage(HttpMethod.Post, AppApiPathConstants.DrivesV1 + "/system/isconfigured");
                var response = await client.SendAsync(request);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Status code was {response.StatusCode}");

                Assert.IsTrue(response.Headers.TryGetValues(corsHeaderName, out var values), "could not find header");

                var value = values.SingleOrDefault();
                Assert.IsNotNull(value);
                Assert.IsTrue(value == $"https://{appCorsHostName}");

                Assert.IsTrue(response.IsSuccessStatusCode);
            }
        }
    }
}