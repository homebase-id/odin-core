using System.Reflection;
using NUnit.Framework;

namespace Odin.Hosting.Tests.YouAuthApi.Drive
{
    public class CommentYouAuthTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
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
        [Ignore("Need api to login via youauth in unit tests")]
        public void CanUploadCommentFromYouAuth()
        {
            Assert.Inconclusive("Need api to login via youauth in unit tests");
        }


        [Test]
        [Ignore("Need api to login via youauth in unit tests")]
        public void FailToUploadStandardFileFromYouAuth()
        {
            Assert.Inconclusive("Need api to login via youauth in unit tests");
        }
    }
}