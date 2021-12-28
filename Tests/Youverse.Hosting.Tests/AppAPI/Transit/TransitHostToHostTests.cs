using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Youverse.Hosting.Tests.AppAPI.Transit
{
    public class TransitHostToHostTests
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
        public void CanGetTransitPublicKeyFromRecipient()
        {
            Assert.Inconclusive("TODO");
        }

        [Test]
        public void MissingTransitPublicKeyWillBeAddedToTransitKeyEncryptionQueue()
        {
            //AddToTransitKeyEncryptionQueue
            Assert.Inconclusive("TODO");
        }

        [Test(Description = "")]
        public Task TestCanRecoverFromRecipientServerOffline()
        {
            return Task.CompletedTask;
        }

    }
}