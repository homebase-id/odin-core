﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.Base.Transit;
using Odin.Hosting.Tests.AppAPI.ApiClient;

namespace Odin.Hosting.Tests.AppAPI.Transit.Query
{
    public class AppTransitQueryPermissionTests
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
        public async Task SystemDefault_AppHas_Read_React_Comment_Permissions_On_AnonymousDrives_WhenConnected()
        {
            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitRead);
            var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
            var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);

            await pippinOwnerClient.Network.SendConnectionRequestTo(merryOwnerClient.Identity);
            await merryOwnerClient.Network.AcceptConnectionRequest(pippinOwnerClient.Identity);

            var allPippinDrivesResponse = await pippinOwnerClient.Drive.GetDrives(1, 200);
            Assert.IsTrue(allPippinDrivesResponse.IsSuccessStatusCode);
            var allPippinDrives = allPippinDrivesResponse.Content;
            Assert.IsNotNull(allPippinDrives);

            var expectedAnonymousDrives = allPippinDrives.Results.Where(drive => drive.AllowAnonymousReads);

            var remoteDotYouContextResponse = await merryAppClient.TransitQuery.GetRemoteDotYouContext(new TransitGetSecurityContextRequest() { OdinId = pippinOwnerClient.Identity.OdinId });
            Assert.IsTrue(remoteDotYouContextResponse.IsSuccessStatusCode);
            var remoteContext = remoteDotYouContextResponse.Content;
            Assert.IsNotNull(remoteContext);
            var groups = remoteContext.PermissionContext.PermissionGroups;
            
            var allDrivesFound = expectedAnonymousDrives.All(ownerAnonDrive =>
                groups.Any(pg => pg.DriveGrants.Any(dg =>
                    dg.PermissionedDrive.Drive == ownerAnonDrive.TargetDriveInfo && 
                    dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Read) && 
                    dg.PermissionedDrive.Permission.HasFlag(DrivePermission.React) && 
                    dg.PermissionedDrive.Permission.HasFlag(DrivePermission.Comment))));
            
            Assert.IsTrue(allDrivesFound);
        }

        [Test]
        public async Task AppFailsTo_GetBatch_OverTransitQuery_Without_UseTransitRead_Permission()
        {
            //Note: I do not prepare any remote data because the permission is enforced on the origin identity
            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitWrite, PermissionKeys.ReadConnections);
            var getBatchResponse = await merryAppClient.TransitQuery.GetBatch(new TransitQueryBatchRequest()
            {
                OdinId = TestIdentities.Pippin.OdinId
            });
            Assert.IsTrue(getBatchResponse.StatusCode == HttpStatusCode.Forbidden, $"status code was {getBatchResponse.StatusCode}");
        }

        [Test]
        public async Task AppFailsTo_GetModified_OverTransitQuery_Without_UseTransitRead_Permission()
        {
            //Note: I do not prepare any remote data because the permission is enforced on the origin identity
            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitWrite, PermissionKeys.ReadConnections);
            var getBatchResponse = await merryAppClient.TransitQuery.GetModified(new TransitQueryModifiedRequest()
            {
                OdinId = TestIdentities.Merry.OdinId,
            });

            Assert.IsTrue(getBatchResponse.StatusCode == HttpStatusCode.Forbidden, $"status code was {getBatchResponse.StatusCode}");
        }


        [Test]
        public async Task AppFailsTo_GetHeader_OverTransitQuery_Without_UseTransitRead_Permission()
        {
            //Note: I do not prepare any remote data because the permission is enforced on the origin identity
            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitWrite, PermissionKeys.ReadConnections);
            var getBatchResponse = await merryAppClient.TransitQuery.GetFileHeader(new TransitExternalFileIdentifier()
            {
                OdinId = TestIdentities.Merry.OdinId,
                File = new()
                {
                    FileId = Guid.NewGuid(),
                    TargetDrive = TargetDrive.NewTargetDrive()
                }
            });

            Assert.IsTrue(getBatchResponse.StatusCode == HttpStatusCode.Forbidden, $"status code was {getBatchResponse.StatusCode}");
        }

        [Test]
        public async Task AppFailsTo_GetPayload_OverTransitQuery_Without_UseTransitRead_Permission()
        {
            //Note: I do not prepare any remote data because the permission is enforced on the origin identity
            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitWrite, PermissionKeys.ReadConnections);
            var getBatchResponse = await merryAppClient.TransitQuery.GetPayload(new TransitGetPayloadRequest()
            {
                OdinId = TestIdentities.Merry.OdinId,
                File = new()
                {
                    FileId = Guid.NewGuid(),
                    TargetDrive = TargetDrive.NewTargetDrive()
                },
                Key = WebScaffold.PAYLOAD_KEY
            });

            Assert.IsTrue(getBatchResponse.StatusCode == HttpStatusCode.Forbidden, $"status code was {getBatchResponse.StatusCode}");
        }


        [Test]
        public async Task AppFailsTo_GetThumbnails_OverTransitQuery_Without_UseTransitRead_Permission()
        {
            //Note: I do not prepare any remote data because the permission is enforced on the origin identity
            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitWrite, PermissionKeys.ReadConnections);
            var getBatchResponse = await merryAppClient.TransitQuery.GetThumbnail(new TransitGetThumbRequest()
            {
                OdinId = TestIdentities.Merry.OdinId,
                File = new()
                {
                    FileId = Guid.NewGuid(),
                    TargetDrive = TargetDrive.NewTargetDrive()
                }
            });

            Assert.IsTrue(getBatchResponse.StatusCode == HttpStatusCode.Forbidden, $"status code was {getBatchResponse.StatusCode}");
        }

        [Test]
        public async Task AppFailsTo_GetBatchCollection_OverTransitQuery_Without_UseTransitRead_Permission()
        {
            //Note: I do not prepare any remote data because the permission is enforced on the origin identity
            var merryAppClient = await this.CreateAppAndClient(TestIdentities.Merry, PermissionKeys.UseTransitWrite, PermissionKeys.ReadConnections);
            var getBatchResponse = await merryAppClient.TransitQuery.GetBatchCollection(new TransitQueryBatchCollectionRequest()
            {
                OdinId = TestIdentities.Merry.OdinId,
                Queries = new List<CollectionQueryParamSection>()
                {
                    new CollectionQueryParamSection()
                    {
                        Name = "test01",
                        QueryParams = default,
                        ResultOptionsRequest = default
                    }
                }
            });

            Assert.IsTrue(getBatchResponse.StatusCode == HttpStatusCode.Forbidden, $"status code was {getBatchResponse.StatusCode}");
        }

        //

        private async Task<AppApiClient> CreateAppAndClient(TestIdentity identity, params int[] permissionKeys)
        {
            var appId = Guid.NewGuid();

            var ownerClient = _scaffold.CreateOwnerApiClient(identity);

            var appDrive = await ownerClient.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some Drive 1", "", false);

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
                PermissionSet = new PermissionSet(permissionKeys)
            };

            await ownerClient.Apps.RegisterApp(appId, appPermissionsGrant);

            var client = _scaffold.CreateAppClient(identity, appId);
            return client;
        }
    }
}