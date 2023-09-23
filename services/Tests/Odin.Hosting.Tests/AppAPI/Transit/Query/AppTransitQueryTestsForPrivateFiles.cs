using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Membership.Circles;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Encryption;
using Odin.Core.Services.Peer.ReceivingHost;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Hosting.Tests.AppAPI.Drive;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.Transit.Query
{
    public class AppTransitQueryTestsForPrivateFiles
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
        public async Task AppCan_Query_Secured_Batch_OverTransitQuery()
        {
            Assert.Fail("TODO");
        }

        [Test]
        public async Task AppCan_Query_Secured_BatchCollection_OverTransitQuery()
        {
            Assert.Fail("TODO");
        }

        [Test]
        public async Task AppCan_Query_Secured_Modified_OverTransitQuery()
        {
            Assert.Fail("TODO");
        }

        [Test]
        public async Task AppCan_Get_Secured_Header_OverTransitQuery()
        {
            Assert.Fail("TODO");
        }

        [Test]
        public async Task AppCan_Get_Secured_Payload_OverTransitQuery()
        {
            Assert.Fail("TODO");
        }

        [Test]
        public async Task AppCan_Get_Secured_Thumbnail_OverTransitQuery()
        {
            Assert.Fail("TODO");
        }

        [Test]
        public async Task AppCan_Get_Secured_Metadata_Type_OverTransitQuery()
        {
            Assert.Fail("TODO");
        }
        
        
        //
        
        private async Task<CircleDefinition> CreateRandomCircle(TestIdentity identity, params int[] permissionKeys)
        {
            var titleId = Guid.NewGuid();
            var ownerClient = _scaffold.CreateOwnerApiClient(identity);

            var appDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), $"Drive for {titleId}", "", false);

            var def = await ownerClient.Membership.CreateCircle($"Random circle {titleId}", new PermissionSetGrantRequest()
            {
                Drives = new DriveGrantRequest[]
                {
                    new()
                    {
                        PermissionedDrive = new()
                        {
                            Drive = appDrive.TargetDriveInfo,
                            Permission = DrivePermission.All
                        }
                    }
                },
                PermissionSet = new PermissionSet(permissionKeys)
            });

            return def;
        }

    }
}