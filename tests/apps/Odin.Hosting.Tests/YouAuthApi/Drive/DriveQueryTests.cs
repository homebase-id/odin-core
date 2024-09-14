using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient;
using Odin.Hosting.Tests.YouAuthApi.ApiClient.Drives;
using Refit;
using QueryModifiedRequest = Odin.Services.Drives.QueryModifiedRequest;

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
        public async Task ShouldNotReturnSecuredFile_QueryBatch()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();

            var targetDrive = TargetDrive.NewTargetDrive();

            await _scaffold.CreateOwnerApiClient(identity).Drive.CreateDrive(targetDrive, "test drive", "", allowAnonymousReads: true); //note: must allow anonymous so youauth can read it
            var securedFileUploadContext = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, AccessControlList.Connected);
            var (anonymousFileUploadResult, _) = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, AccessControlList.Anonymous);

            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
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
            Assert.True(batch.SearchResults.Single().FileId == anonymousFileUploadResult.File.FileId);
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
                RequiredSecurityGroup = SecurityGroupType.ConfirmConnected
            };

            var securedFileUploadContext = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, AccessControlList.Connected);
            var (anonymousFileUploadResult, _) = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, AccessControlList.Anonymous);
            var securedFileUploadContext2 = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, circleSecuredAcl);

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

            var request = new QueryBatchRequest()
            {
                QueryParams = qp,
                ResultOptionsRequest = resultOptions
            };

            // var anonymousApiClient = new UniversalDriveApiClient(identity.OdinId, new GuestApiClientFactory());
            // var response = await anonymousApiClient.QueryBatch(request);
            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            var svc = RestService.For<IRefitGuestDriveQuery>(client);
            var response = await svc.GetBatch(request);
            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            var batch = response.Content;

            Assert.IsNotNull(batch);
            Assert.True(batch.SearchResults.Count() == 1); //should only be the anonymous file we uploaded
            Assert.True(batch.SearchResults.Single().FileId == anonymousFileUploadResult.File.FileId);
        }

        [Test]
        public async Task ShouldNotReturnSecuredFile_QueryModified()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var targetDrive = TargetDrive.NewTargetDrive();

            var ownerApiClient = _scaffold.CreateOwnerApiClient(identity);
            await ownerApiClient.Drive.CreateDrive(targetDrive, "test drive", "", true); //note: must allow anonymous so youauth can read it

            var securedFileUploadContext = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, AccessControlList.Connected);
            var anonymousFileUploadContext = await this.UploadFile2(identity.OdinId, targetDrive, null, tag, AccessControlList.Anonymous);

            //overwrite them to ensure the updated timestamp is set
            await this.UploadFile2(identity.OdinId, targetDrive,
                overwriteFileId: securedFileUploadContext.uploadResult.File.FileId,
                tag,
                AccessControlList.Connected, versionTag: securedFileUploadContext.uploadResult.NewVersionTag);

            anonymousFileUploadContext = await this.UploadFile2(identity.OdinId, targetDrive,
                overwriteFileId: anonymousFileUploadContext.uploadResult.File.FileId,
                tag,
                AccessControlList.Anonymous, versionTag: anonymousFileUploadContext.uploadResult.NewVersionTag);

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
                    IncludeHeaderContent = false
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
                Assert.True(batch.SearchResults.Count() == 1, $"Actual count was {batch.SearchResults.Count()}"); //should only be the anonymous file we uploaded
                Assert.True(batch.SearchResults.Single().FileId == anonymousFileUploadContext.uploadResult.File.FileId);
            }
        }

        [Test]
        public async Task CanQueryBatchByOneTag()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var (uploadResult, uploadFileMetadata) = await this.UploadFile(identity.OdinId, tag, SecurityGroupType.Anonymous);

            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            {
                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadResult.File.TargetDrive,
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
            var (uploadResult, uploadFileMetadata) = await this.UploadFile(identity.OdinId, tag, SecurityGroupType.Anonymous);

            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            {
                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadResult.File.TargetDrive,
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

                Assert.IsTrue(firstResult.FileMetadata.AppData.FileType == uploadFileMetadata.AppData.FileType);
                Assert.IsTrue(firstResult.FileMetadata.AppData.DataType == uploadFileMetadata.AppData.DataType);
                Assert.IsTrue(firstResult.FileMetadata.AppData.UserDate == uploadFileMetadata.AppData.UserDate);
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
            var (uploadResult, uploadFileMetadata) = await this.UploadFile(identity.OdinId, tag, SecurityGroupType.Anonymous);

            var client = _scaffold.CreateAnonymousApiHttpClient(identity.OdinId);
            {
                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadResult.File.TargetDrive,
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

        private async Task<(UploadResult uploadResult, UploadFileMetadata uploadedFileMetadata)> UploadFile(OdinId identity, Guid tag,
            SecurityGroupType requiredSecurityGroup)
        {
            List<Guid> tags = new List<Guid>() { tag };

            var uploadFileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = false,
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

            var client = _scaffold.CreateOwnerApiClient(TestIdentities.All[identity]);
            var td = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "a drive", "", true);
            var response = await client.DriveRedux.UploadNewMetadata(td.TargetDriveInfo, uploadFileMetadata);
            var uploadResult = response.Content;
            return (uploadResult, uploadFileMetadata);
        }

        private async Task<(UploadResult uploadResult, UploadFileMetadata uploadedFileMetadata)> UploadFile2(OdinId identity,
            TargetDrive drive,
            Guid? overwriteFileId,
            Guid tag,
            AccessControlList acl,
            Guid? versionTag = null)
        {
            var uploadFileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = false,
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

            var client = _scaffold.CreateOwnerApiClient(TestIdentities.All[identity]);

            if (overwriteFileId.HasValue)
            {
                var targetFile = new ExternalFileIdentifier()
                {
                    TargetDrive = drive,
                    FileId = overwriteFileId.GetValueOrDefault()
                };
                
                var response = await client.DriveRedux.UpdateExistingMetadata(targetFile, versionTag.GetValueOrDefault(), uploadFileMetadata);
                var uploadResult = response.Content;
                return (uploadResult, uploadFileMetadata);
            }
            else
            {
                var response = await client.DriveRedux.UploadNewMetadata(drive, uploadFileMetadata);
                var uploadResult = response.Content;
                return (uploadResult, uploadFileMetadata);
            }
        }
    }
}