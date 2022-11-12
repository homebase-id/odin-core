using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers;

namespace Youverse.Hosting.Tests.DriveApi.YouAuth
{
    public class DriveQueryTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
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
            await _scaffold.OwnerApi.CreateDrive(identity.DotYouId, targetDrive, "test drive", "", true); //note: must allow anonymous so youauth can read it
            var securedFileUploadContext = await this.UploadFile2(identity.DotYouId, targetDrive, null, tag, SecurityGroupType.Connected, "payload");
            var anonymousFileUploadContext = await this.UploadFile2(identity.DotYouId, targetDrive, null, tag, SecurityGroupType.Anonymous, "another payload");

            //overwrite them to ensure the updated timestamp is set

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity.DotYouId))
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

                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);
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
        // [Ignore("invalid test until we support file updates")]
        public async Task ShouldNotReturnSecuredFile_QueryModified()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();

            var targetDrive = TargetDrive.NewTargetDrive();
            await _scaffold.OwnerApi.CreateDrive(identity.DotYouId, targetDrive, "test drive", "", true); //note: must allow anonymous so youauth can read it
            var securedFileUploadContext = await this.UploadFile2(identity.DotYouId, targetDrive, null, tag, SecurityGroupType.Connected, "payload");
            var anonymousFileUploadContext = await this.UploadFile2(identity.DotYouId, targetDrive, null, tag, SecurityGroupType.Anonymous, "another payload");

            //overwrite them to ensure the updated timestamp is set
            securedFileUploadContext = await this.UploadFile2(identity.DotYouId, targetDrive, securedFileUploadContext.UploadedFile.FileId, tag, SecurityGroupType.Connected, "payload");
            anonymousFileUploadContext = await this.UploadFile2(identity.DotYouId, targetDrive, anonymousFileUploadContext.UploadedFile.FileId, tag, SecurityGroupType.Anonymous, "payload");

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity.DotYouId))
            {
                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);

                var qp = new FileQueryParams()
                {
                    TargetDrive = targetDrive,
                    TagsMatchAtLeastOne = new List<Guid>() { tag }
                };

                var resultOptions = new QueryModifiedResultOptions()
                {
                    MaxDate = (UInt64)DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds(),
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
                Assert.True(batch.SearchResults.Count() == 1, $"Actual count was {batch.SearchResults.Count()}"); //should only be the anonymous file we uploaded
                Assert.True(batch.SearchResults.Single().FileId == anonymousFileUploadContext.UploadedFile.FileId);
            }
        }

        [Test]
        public async Task CanQueryBatchByOneTag()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var uploadContext = await this.UploadFile(identity.DotYouId, tag, SecurityGroupType.Anonymous);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity.DotYouId))
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

                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);
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
            var uploadContext = await this.UploadFile(identity.DotYouId, tag, SecurityGroupType.Anonymous);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity.DotYouId))
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

                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);
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
                Assert.NotNull(firstResult.FileMetadata.AppData.JsonContent);
                Assert.IsNotEmpty(firstResult.FileMetadata.AppData.JsonContent);

                Assert.IsTrue(firstResult.FileMetadata.AppData.FileType == uploadContext.UploadFileMetadata.AppData.FileType);
                Assert.IsTrue(firstResult.FileMetadata.AppData.DataType == uploadContext.UploadFileMetadata.AppData.DataType);
                Assert.IsTrue(firstResult.FileMetadata.AppData.UserDate == uploadContext.UploadFileMetadata.AppData.UserDate);
                Assert.IsTrue(firstResult.FileMetadata.ContentType == uploadContext.UploadFileMetadata.ContentType);
                Assert.IsTrue(string.IsNullOrEmpty(firstResult.FileMetadata.SenderDotYouId));

                //must be ordered correctly
                //TODO: How to test this with a fileId?
            }
        }

        [Test]
        public async Task CanQueryDriveModifiedItemsRedactedContent()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var uploadContext = await this.UploadFile(identity.DotYouId, tag, SecurityGroupType.Anonymous);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity.DotYouId))
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

                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);
                var request = new QueryBatchRequest()
                {
                    QueryParams = qp,
                    ResultOptionsRequest = resultOptions
                };

                var response = await svc.GetBatch(request);


                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;
                Assert.IsNotNull(batch);
                Assert.IsTrue(batch.SearchResults.All(item => string.IsNullOrEmpty(item.FileMetadata.AppData.JsonContent)), "One or more items had content");
            }
        }

        private async Task<UploadTestUtilsContext> UploadFile(DotYouIdentity identity, Guid tag, SecurityGroupType requiredSecurityGroup)
        {
            List<Guid> tags = new List<Guid>() { tag};

            var uploadFileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                PayloadIsEncrypted = false,
                AppData = new()
                {
                    ContentIsComplete = false,
                    JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = 0,
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

            return await _scaffold.OwnerApi.Upload(identity, uploadFileMetadata, options);
        }

        private async Task<UploadTestUtilsContext> UploadFile2(DotYouIdentity identity, TargetDrive drive, Guid? overwriteFileId, Guid tag, SecurityGroupType requiredSecurityGroup, string payload)
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
                ContentType = "application/json",
                PayloadIsEncrypted = false,
                AppData = new()
                {
                    ContentIsComplete = false,
                    JsonContent = DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = 0,
                    Tags = new List<Guid>() { tag }
                },
                AccessControlList = new AccessControlList()
                {
                    RequiredSecurityGroup = requiredSecurityGroup
                }
            };

            return await _scaffold.OwnerApi.UploadFile(identity, instructionSet, uploadFileMetadata, payload, false);
        }
    }
}