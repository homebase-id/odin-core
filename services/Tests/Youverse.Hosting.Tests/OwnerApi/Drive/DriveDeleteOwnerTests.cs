using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Query;
using Youverse.Core.Services.Drives.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers;

namespace Youverse.Hosting.Tests.OwnerApi.Drive
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
        public async Task CanSoftDeleteFile()
        {
        }

        [Test]
        [Ignore("TODO")]
        public async Task CanHardDeleteFile()
        {
            
        }
    }
}