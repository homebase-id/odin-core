using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Core.Time;

namespace Odin.Hosting.Tests.AppAPI.Drive
{
    public class DriveQuerySecurityTests
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

        private async Task<(UploadResult uploadResult, UploadFileMetadata uploadedMetadata)> UploadStandardRandomFileHeadersUsingOwnerApi(TestIdentity identity,
            TargetDrive targetDrive, AccessControlList acl = null)
        {
            var client = _scaffold.CreateOwnerApiClient(identity);
            var fileMetadata = new UploadFileMetadata()
            {
                IsEncrypted = false,
                AllowDistribution = false,
                AppData = new()
                {
                    FileType = 777,
                    Content = $"Some json content {Guid.NewGuid()}",
                    UniqueId = Guid.NewGuid(),
                },
                AccessControlList = acl ?? AccessControlList.OwnerOnly
            };

            var result = await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata);
            return (result, fileMetadata);
        }
    }
}
