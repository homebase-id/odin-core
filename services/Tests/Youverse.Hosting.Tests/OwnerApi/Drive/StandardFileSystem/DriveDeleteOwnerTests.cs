using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Youverse.Hosting.Tests.OwnerApi.Drive.StandardFileSystem
{
    public class DriveDeleteOwnerTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }
        
        [Test]
        [Ignore("TODO")]
        public void CanSoftDeleteFile()
        {
        }

        [Test]
        [Ignore("TODO")]
        public void CanHardDeleteFile()
        {
            
        }
    }
}