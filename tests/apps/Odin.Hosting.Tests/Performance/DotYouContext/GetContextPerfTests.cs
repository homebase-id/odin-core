using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Hosting.Tests.Anonymous.Ident;
using Odin.Hosting.Tests.AppAPI.ApiClient;
using Refit;

namespace Odin.Hosting.Tests.Performance.DotYouContext
{
    [TestFixture]
    public class GetContextPerfTests
    {
        private static readonly int MAXTHREADS = 12;
        private const int MAXITERATIONS = 150;

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
            _scaffold.ClearAssertLogEventsAction();
            _scaffold.ClearLogEvents();
        }

        [TearDown]
        public void TearDown()
        {
            _scaffold.AssertLogEvents();
        }


        [Test]
        public async Task CanGetAnonymousIdentInfo()
        {
            var anonClient = _scaffold.CreateAnonymousApiHttpClient(TestIdentities.Samwise.OdinId);

            var svc = RestService.For<IIdentHttpClient>(anonClient);

            var identResponse = await svc.GetIdent();
            var ident = identResponse.Content;
            ClassicAssert.IsFalse(string.IsNullOrEmpty(ident.OdinId));
            ClassicAssert.IsTrue(ident.Version == 1.0);
        }

        [Test]
        public async Task CanGetAppSecurityContext()
        {
            var (appApiClient, drive) = await CreateApp(TestIdentities.Samwise);
            var context = await appApiClient.Security.GetSecurityContext();
            ClassicAssert.IsFalse(string.IsNullOrEmpty(context.Caller.OdinId));
        }

        /*  After DB caching
         * AppPingTest
               Duration:?8.3 sec

                Standard Output:?
                2023-06-01 Host [SEMIBEASTII]
                Threads   : 12
                Iterations: 1,500
                Wall Time : 8,249ms
                Minimum   : 1ms
                Maximum   : 39ms
                Average   : 4ms
                Median    : 3ms
                Capacity  : 2,182 / second
                RSA Encryptions 0, Decryptions 8
                RSA Keys Created 4, Keys Expired 0
                DB Opened 4, Closed 0
         */
        [Test]
        public async Task AppPingTest()
        {
            await PerformanceFramework.ThreadedTestAsync(MAXTHREADS, MAXITERATIONS, AppPing);
            Assert.Pass();
        }

        public async Task<(long, long[])> AppPing(int threadno, int iterations)
        {
            long[] timers = new long[iterations];
            Debug.Assert(timers.Length == iterations);
            var sw = new Stopwatch();

            var ownerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

            for (int count = 0; count < iterations; count++)
            {
                sw.Restart();

                var context = await ownerClient.Security.GetSecurityContext();
                ClassicAssert.IsFalse(string.IsNullOrEmpty(context.Caller.OdinId));

                timers[count] = sw.ElapsedMilliseconds;
            }

            return (0, timers);
        }

        private async Task<(AppApiClient appApiClient, TargetDrive drive)> CreateApp(TestIdentity identity)
        {
            var ownerClient = _scaffold.CreateOwnerApiClient(identity);
            var appDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some app Drive 1", "", false);
            var appId = Guid.NewGuid();

            var appPermissionsGrant = new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = appDrive.TargetDriveInfo,
                            Permission = DrivePermission.All
                        }
                    }
                },
                PermissionSet = new PermissionSet(PermissionKeys.All)
            };

            var appRegistration = await ownerClient.Apps.RegisterApp(appId, appPermissionsGrant);
            var appApiClient = _scaffold.CreateAppClient(TestIdentities.Samwise, appId);

            return (appApiClient, appDrive.TargetDriveInfo);
        }
    }
}