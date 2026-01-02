using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Odin.Hosting.Tests.OwnerApi.VersionUpgrade
{
    public class VersionUpgradeTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var env = new Dictionary<string, string>
            {
                { "Development__VersionUpgradeTestModeEnabled", "True" },
            };

            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests(
                envOverrides: env,
                initializeIdentity: true, setupOwnerAccounts: true,
                testIdentities:
                [
                    TestIdentities.Merry,
                ]);
            _scaffold.AssertLogEvents();
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
        public async Task WillKeepLatestSuccessfulVersionNumber()
        {
            // expect the one testmode error
            _scaffold.SetAssertLogEventsAction(logEvents =>
            {
                var errorLogs = logEvents[Serilog.Events.LogEventLevel.Error];
                Assert.That(errorLogs.Count, Is.EqualTo(1), "Unexpected number of Error log events");
                foreach (var error in errorLogs)
                {
                    Assert.That(error.Exception!.Message, Is.EqualTo("Forced VersionUpgrade Failure for automated tests"));
                }
            });

            // enter recovery mode
            var merry = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);

            var latestVersionResponse = await merry.VersionClient.GetVersionInfo();
            ClassicAssert.IsTrue(latestVersionResponse.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(latestVersionResponse.Content);
            ClassicAssert.IsTrue(latestVersionResponse.Content!.ActualDataVersionNumber ==
                                 latestVersionResponse.Content.ServerDataVersionNumber);

            // system starts at latest version so downgrade to the start
            var setVersionResponse = await merry.VersionClient.ForceVersionNumber(1);
            ClassicAssert.IsTrue(setVersionResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(setVersionResponse.Content!.DataVersionNumber == 1);

            // test modes means the upgrade will die when after upgrading to v4
            var forceVersionUpgradeResponse = await merry.VersionClient.ForceVersionUpgrade();
            ClassicAssert.IsTrue(forceVersionUpgradeResponse.IsSuccessStatusCode);

            // this delay is to get the job run via force condition
            await Task.Delay(1000);

            const int expectedVersionNumber = 4;
            var pleaseBeAtVersion4Response = await merry.VersionClient.GetVersionInfo();
            ClassicAssert.IsTrue(pleaseBeAtVersion4Response.IsSuccessStatusCode);
            ClassicAssert.IsNotNull(pleaseBeAtVersion4Response.Content);
            var actualVersion = pleaseBeAtVersion4Response.Content!.ActualDataVersionNumber;
            ClassicAssert.IsTrue(actualVersion == expectedVersionNumber,
                $"Version number should have been {expectedVersionNumber} but was {actualVersion}");
        }
    }
}