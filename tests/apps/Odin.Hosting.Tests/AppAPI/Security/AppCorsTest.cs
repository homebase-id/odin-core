using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Controllers.ClientToken;
using Odin.Hosting.Controllers.ClientToken.App;

namespace Odin.Hosting.Tests.AppAPI.Security
{
    public class AppCorsTest
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo });
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
        public async Task CorsMiddleWareInjectsAppCorsHostNameForAnyAppPathWithGetVerb()
        {
            Guid appId = Guid.NewGuid();
            const string appCorsHostName = "app.domain.org";
            var appContext = await _scaffold.OldOwnerApi.SetupTestSampleApp(appId,
                TestIdentities.Frodo,
                canReadConnections: true,
                appCorsHostName: appCorsHostName);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(appContext);
            {
                const string corsHeaderName = "Access-Control-Allow-Origin";
                var request = new HttpRequestMessage(HttpMethod.Get, $"{AppApiPathConstants.AuthV1}/verifytoken");
                var response = await client.SendAsync(request);

                ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Status code was {response.StatusCode}");

                ClassicAssert.IsTrue(response.Headers.TryGetValues(corsHeaderName, out var values), "could not find header");

                var value = values.SingleOrDefault();
                ClassicAssert.IsNotNull(value);
                ClassicAssert.IsTrue(value == $"https://{appCorsHostName}:{WebScaffold.HttpsPort}");
                ClassicAssert.IsTrue(response.IsSuccessStatusCode);
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

            var client = _scaffold.AppApi.CreateAppApiHttpClient(appContext);
            {
                const string corsHeaderName = "Access-Control-Allow-Origin";
                var request = new HttpRequestMessage(HttpMethod.Post, AppApiPathConstants.DriveV1 + "/system/isconfigured");
                var response = await client.SendAsync(request);

                ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Status code was {response.StatusCode}");

                ClassicAssert.IsTrue(response.Headers.TryGetValues(corsHeaderName, out var values), "could not find header");

                var value = values.SingleOrDefault();
                ClassicAssert.IsNotNull(value);
                ClassicAssert.IsTrue(value == $"https://{appCorsHostName}:{WebScaffold.HttpsPort}");
                ClassicAssert.IsTrue(response.IsSuccessStatusCode);
            }
        }
    }
}