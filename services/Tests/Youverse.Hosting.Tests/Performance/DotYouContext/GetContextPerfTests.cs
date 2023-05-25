using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Hosting.Tests.Anonymous.Ident;
using Youverse.Hosting.Tests.AppAPI.ApiClient;

namespace Youverse.Hosting.Tests.Performance.DotYouContext
{
    [TestFixture]
    public class GetContextPerfTests
    {
        private static readonly int MAXTHREADS = 12;
        private const int MAXITERATIONS = 1500;

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
        public async Task CanGetAnonymousIdentInfo()
        {
            var anonClient = _scaffold.CreateAnonymousApiHttpClient(TestIdentities.Samwise.OdinId);

            var svc = RestService.For<IIdentHttpClient>(anonClient);

            var identResponse = await svc.GetIdent();
            var ident = identResponse.Content;
            Assert.IsFalse(string.IsNullOrEmpty(ident.OdinId));
            Assert.IsTrue(ident.Version == 1.0);
        }

        [Test]
        public async Task CanGetAppSecurityContext()
        {
            var (appApiClient, drive) = await CreateApp(TestIdentities.Samwise);
            var context = await appApiClient.Security.GetSecurityContext();
            Assert.IsFalse(string.IsNullOrEmpty(context.Caller.OdinId));
        }

        [Test]
        public void AppPingTest()
        {
            PerformanceFramework.ThreadedTest(MAXTHREADS, MAXITERATIONS, AppPing);
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
                Assert.IsFalse(string.IsNullOrEmpty(context.Caller.OdinId));

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