using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Time;
using Odin.Hosting.Tests.YouAuthApi.ApiClient.Drives;
using Refit;
using QueryModifiedRequest = Odin.Core.Services.Drives.QueryModifiedRequest;

namespace Odin.Hosting.Tests.YouAuthApi.Drive
{
    public class DriveQueryTests
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
        public async Task ShouldNotReturnSecuredFile_QueryBatch()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();

            var targetDrive = TargetDrive.NewTargetDrive();
            await _scaffold.OldOwnerApi.CreateDrive(identity.OdinId, targetDrive, "test drive", "", true); //note: must allow anonymous so youauth can read it
            var securedFileUploadContext = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, AccessControlList.Connected, "payload");
            var anonymousFileUploadContext = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, AccessControlList.Anonymous, "another payload");

            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            {
                var qp = new FileQueryParams()
                {
                    TargetDrive = securedFileUploadContext.UploadedFile.TargetDrive,
                    TagsMatchAtLeastOne = new List<Guid>() { tag }
                };

                var resultOptions = new QueryBatchResultOptionsRequest()
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = false
                };

                var svc = RestService.For<IRefitGuestDriveQuery>(client);
                var request = new QueryBatchRequest()
                {
                    QueryParams = qp,
                    ResultOptionsRequest = resultOptions
                };

                var response = await svc.GetBatch(request);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;

                Assert.IsNotNull(batch);
                Assert.True(batch.SearchResults.Count() == 1); //should only be the anonymous file we uploaded
                Assert.True(batch.SearchResults.Single().FileId == anonymousFileUploadContext.UploadedFile.FileId);
            }
        }

        [Test]
        public async Task ShouldNotReturnSecuredFile_QueryBatch_WhenSecuredWithCircle()
        {
            var identity = TestIdentities.Samwise;
            var ownerApiClient = _scaffold.CreateOwnerApiClient(identity);

            var tag = Guid.NewGuid();
            var targetDrive = TargetDrive.NewTargetDrive();

            await ownerApiClient.Drive.CreateDrive(targetDrive, "test drive", "", true); //note: must allow anonymous so youauth can read it
            var circle = await ownerApiClient.Membership.CreateCircle("Security Circle", targetDrive, DrivePermission.None);
            var circleSecuredAcl = new AccessControlList()
            {
                CircleIdList = new List<Guid>()
                {
                    circle.Id
                },
                RequiredSecurityGroup = SecurityGroupType.Connected
            };
            
            var securedFileUploadContext = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, AccessControlList.Connected, "payload");
            var anonymousFileUploadContext = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, AccessControlList.Anonymous, "another payload");
            var securedFileUploadContext2 = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, circleSecuredAcl, "payload 2");

            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            {
                var qp = new FileQueryParams()
                {
                    TargetDrive = targetDrive,
                    TagsMatchAtLeastOne = new List<Guid>() { tag }
                };

                var resultOptions = new QueryBatchResultOptionsRequest()
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = false
                };

                var svc = RestService.For<IRefitGuestDriveQuery>(client);
                var request = new QueryBatchRequest()
                {
                    QueryParams = qp,
                    ResultOptionsRequest = resultOptions
                };

                var response = await svc.GetBatch(request);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;

                Assert.IsNotNull(batch);
                Assert.True(batch.SearchResults.Count() == 1); //should only be the anonymous file we uploaded
                Assert.True(batch.SearchResults.Single().FileId == anonymousFileUploadContext.UploadedFile.FileId);
            }
        }

        [Test]
        public async Task ShouldNotReturnSecuredFile_QueryModified()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();

            var targetDrive = TargetDrive.NewTargetDrive();
            await _scaffold.OldOwnerApi.CreateDrive(identity.OdinId, targetDrive, "test drive", "", true); //note: must allow anonymous so youauth can read it
            var securedFileUploadContext = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, AccessControlList.Connected, "payload");
            var anonymousFileUploadContext = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, AccessControlList.Anonymous, "another payload");

            //overwrite them to ensure the updated timestamp is set
            securedFileUploadContext = await this.UploadFile2(identity.OdinId, targetDrive, securedFileUploadContext.UploadedFile.FileId, tag,
                AccessControlList.Connected, "payload", versionTag: securedFileUploadContext.UploadResult.NewVersionTag);
            anonymousFileUploadContext = await this.UploadFile2(identity.OdinId, targetDrive, anonymousFileUploadContext.UploadedFile.FileId, tag,
                AccessControlList.Anonymous, "payload", versionTag: anonymousFileUploadContext.UploadResult.NewVersionTag);

            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            {
                var svc = RestService.For<IRefitGuestDriveQuery>(client);

                var qp = new FileQueryParams()
                {
                    TargetDrive = targetDrive,
                    TagsMatchAtLeastOne = new List<Guid>() { tag }
                };

                var resultOptions = new QueryModifiedResultOptions()
                {
                    MaxDate = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds(),
                    MaxRecords = 10,
                    IncludeJsonContent = false
                };

                var request = new QueryModifiedRequest()
                {
                    QueryParams = qp,
                    ResultOptions = resultOptions
                };

                var getModifiedResponse = await svc.GetModified(request);
                Assert.IsTrue(getModifiedResponse.IsSuccessStatusCode, $"Failed status code.  Value was {getModifiedResponse.StatusCode}");
                var batch = getModifiedResponse.Content;

                Assert.IsNotNull(batch);
                Assert.True(batch.SearchResults.Count() == 1,
                    $"Actual count was {batch.SearchResults.Count()}"); //should only be the anonymous file we uploaded
                Assert.True(batch.SearchResults.Single().FileId == anonymousFileUploadContext.UploadedFile.FileId);
            }
        }

        [Test]
        public async Task CanQueryBatchByOneTag()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var uploadContext = await this.UploadFile(identity.OdinId, tag, SecurityGroupType.Anonymous);

            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            {
                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.UploadedFile.TargetDrive,
                    TagsMatchAtLeastOne = new List<Guid>() { tag }
                };

                var resultOptions = new QueryBatchResultOptionsRequest()
                {
                    CursorState = "",
                    MaxRecords = 10,
                    IncludeMetadataHeader = false
                };

                var svc = RestService.For<IRefitGuestDriveQuery>(client);
                var request = new QueryBatchRequest()
                {
                    QueryParams = qp,
                    ResultOptionsRequest = resultOptions
                };

                var response = await svc.GetBatch(request);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;

                Assert.IsNotNull(batch);
                Assert.IsNotNull(batch.SearchResults.Single(item => item.FileMetadata.AppData.Tags.Any(t => t == tag)));
            }
        }

        [Test]
        public async Task CanQueryDriveModifiedItems()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var uploadContext = await this.UploadFile(identity.OdinId, tag, SecurityGroupType.Anonymous);

            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            {
                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.UploadedFile.TargetDrive,
                };

                var resultOptions = new QueryBatchResultOptionsRequest()
                {
                    CursorState = "",
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
                };

                var svc = RestService.For<IRefitGuestDriveQuery>(client);
                var request = new QueryBatchRequest()
                {
                    QueryParams = qp,
                    ResultOptionsRequest = resultOptions
                };

                var response = await svc.GetBatch(request);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;
                Assert.IsNotNull(batch);

                //TODO: what to test here?
                Assert.IsTrue(batch.SearchResults.Any());
                Assert.IsNotNull(batch.CursorState);
                Assert.IsNotEmpty(batch.CursorState);

                var firstResult = batch.SearchResults.First();

                //ensure file content was sent 
                Assert.NotNull(firstResult.FileMetadata.AppData.Content);
                Assert.IsNotEmpty(firstResult.FileMetadata.AppData.Content);

                Assert.IsTrue(firstResult.FileMetadata.AppData.FileType == uploadContext.UploadFileMetadata.AppData.FileType);
                Assert.IsTrue(firstResult.FileMetadata.AppData.DataType == uploadContext.UploadFileMetadata.AppData.DataType);
                Assert.IsTrue(firstResult.FileMetadata.AppData.UserDate == uploadContext.UploadFileMetadata.AppData.UserDate);
                Assert.IsTrue(string.IsNullOrEmpty(firstResult.FileMetadata.SenderOdinId));

                //must be ordered correctly
                //TODO: How to test this with a fileId?
            }
        }

        [Test]
        public async Task CanQueryDriveModifiedItemsRedactedContent()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var uploadContext = await this.UploadFile(identity.OdinId, tag, SecurityGroupType.Anonymous);

            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            {
                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.UploadedFile.TargetDrive,
                };

                var resultOptions = new QueryBatchResultOptionsRequest()
                {
                    CursorState = "",
                    MaxRecords = 10,
                    IncludeMetadataHeader = false
                };

                var svc = RestService.For<IRefitGuestDriveQuery>(client);
                var request = new QueryBatchRequest()
                {
                    QueryParams = qp,
                    ResultOptionsRequest = resultOptions
                };

                var response = await svc.GetBatch(request);


                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;
                Assert.IsNotNull(batch);
                Assert.IsTrue(batch.SearchResults.All(item => string.IsNullOrEmpty(item.FileMetadata.AppData.Content)), "One or more items had content");
            }
        }

        private async Task<UploadTestUtilsContext> UploadFile(OdinId identity, Guid tag, SecurityGroupType requiredSecurityGroup)
        {
            List<Guid> tags = new List<Guid>() { tag };

            var uploadFileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                PayloadIsEncrypted = false,
                AppData = new()
                {
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0),
                    Tags = tags
                },
                AccessControlList = new AccessControlList()
                {
                    RequiredSecurityGroup = requiredSecurityGroup
                }
            };

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                EncryptPayload = false,
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = false,
                DriveAllowAnonymousReads = true
            };

            return await _scaffold.OldOwnerApi.Upload(identity, uploadFileMetadata, options);
        }

        private async Task<UploadTestUtilsContext> UploadFile2(OdinId identity, TargetDrive drive, Guid? overwriteFileId, Guid tag,
            AccessControlList acl, string payload, Guid? versionTag = null)
        {
            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = ByteArrayUtil.GetRndByteArray(16),
                StorageOptions = new StorageOptions()
                {
                    Drive = drive,
                    OverwriteFileId = overwriteFileId
                },
                TransitOptions = null
            };

            var uploadFileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                PayloadIsEncrypted = false,
                VersionTag = versionTag,
                AppData = new()
                {
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0),
                    Tags = new List<Guid>() { tag }
                },
                AccessControlList = acl
            };

            return await _scaffold.OldOwnerApi.UploadFile(identity, instructionSet, uploadFileMetadata, payload, false);
        }
    }
}