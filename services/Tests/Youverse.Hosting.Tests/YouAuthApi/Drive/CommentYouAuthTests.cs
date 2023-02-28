using System;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Hosting.Tests.DriveApi.YouAuth;

namespace Youverse.Hosting.Tests.YouAuthApi.Drive
{
    public class CommentYouAuthTests
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
        [Ignore("Need api to login via youauth in unit tests")]
        public async Task CanUploadCommentFromYouAuth()
        {
            Assert.Inconclusive("Need api to login via youauth in unit tests");
        }


        [Test]
        [Ignore("Need api to login via youauth in unit tests")]
        public async Task FailToUploadStandardFileFromYouAuth()
        {
            Assert.Inconclusive("Need api to login via youauth in unit tests");
        }
    }
}