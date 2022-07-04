using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit.Upload;

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

            var securedFileUploadContext = await this.UploadFile(identity, tag, SecurityGroupType.Connected);
            var anonymousFileUploadContext = await this.UploadFile(identity, tag, SecurityGroupType.Anonymous);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);

                var startCursor = Array.Empty<byte>();
                var stopCursor = Array.Empty<byte>();
                var qp = new QueryParams()
                {
                    TagsMatchAtLeastOne = new List<byte[]>() { tag.ToByteArray() }
                };

                var resultOptions = new ResultOptions()
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = false
                };

                var getBatchResponse = await svc.GetBatch(securedFileUploadContext.UploadedFile.TargetDrive, startCursor, stopCursor, qp, resultOptions);
                Assert.IsTrue(getBatchResponse.IsSuccessStatusCode, $"Failed status code.  Value was {getBatchResponse.StatusCode}");
                var batch = getBatchResponse.Content;

                Assert.IsNotNull(batch);
                Assert.True(batch.SearchResults.Count() == 1); //should only be the anonymous file we uploaded
                Assert.True(batch.SearchResults.Single().FileId == anonymousFileUploadContext.UploadedFile.FileId);
            }
        }

        [Test]
        public async Task ShouldNotReturnSecuredFile_QueryRecent()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            
            var targetDrive = TargetDrive.NewTargetDrive();
            await _scaffold.OwnerApi.CreateDrive(identity, targetDrive, "test drive", "", true); //note: must allow anonymous so youauth can read it
            var securedFileUploadContext = await this.UploadFile2(identity, targetDrive, null, tag, SecurityGroupType.Connected, "payload");
            var anonymousFileUploadContext = await this.UploadFile2(identity,targetDrive, null, tag, SecurityGroupType.Anonymous, "another payload");

            //overwrite them to ensure the updated timestamp is set
            securedFileUploadContext = await this.UploadFile2(identity, targetDrive, securedFileUploadContext.UploadedFile.FileId, tag, SecurityGroupType.Connected, "payload");
            anonymousFileUploadContext = await this.UploadFile2(identity, targetDrive, anonymousFileUploadContext.UploadedFile.FileId, tag, SecurityGroupType.Anonymous, "payload");

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);

                var startCursor = Array.Empty<byte>();
                var qp = new QueryParams()
                {
                    TagsMatchAtLeastOne = new List<byte[]>() { tag.ToByteArray() }
                };

                var resultOptions = new ResultOptions()
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = false
                };

                var maxDateInPast = (UInt64)DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();

                var getRecentResponse = await svc.GetRecent(targetDrive, maxDateInPast, startCursor, qp, resultOptions);
                Assert.IsTrue(getRecentResponse.IsSuccessStatusCode, $"Failed status code.  Value was {getRecentResponse.StatusCode}");
                var batch = getRecentResponse.Content;

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
            var uploadContext = await this.UploadFile(identity, tag, SecurityGroupType.Anonymous);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var startCursor = Array.Empty<byte>();
                var stopCursor = Array.Empty<byte>();
                var qp = new QueryParams()
                {
                    TagsMatchAtLeastOne = new List<byte[]>() { tag.ToByteArray() }
                };

                var resultOptions = new ResultOptions()
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = false
                };

                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);
                var response = await svc.GetBatch(uploadContext.UploadedFile.TargetDrive, startCursor, stopCursor, qp, resultOptions);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;

                Assert.IsNotNull(batch);
                Assert.IsNotNull(batch.SearchResults.Single(item => item.Tags.Contains(tag)));
            }
        }

        [Test]
        public async Task CanQueryDriveRecentItems()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var uploadContext = await this.UploadFile(identity, tag, SecurityGroupType.Anonymous);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);

                var startCursor = Array.Empty<byte>();
                var stopCursor = Array.Empty<byte>();
                var qp = new QueryParams();
                var resultOptions = new ResultOptions()
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
                };

                var response = await svc.GetBatch(uploadContext.UploadedFile.TargetDrive, startCursor, stopCursor, qp, resultOptions);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;
                Assert.IsNotNull(batch);

                //TODO: what to test here?
                Assert.IsTrue(batch.SearchResults.Any());
                Assert.IsNotNull(batch.StartCursor);
                //TODO: test that star cursor is not zeros

                //TODO: ensure cursor was updated sometime in the last 10 minutes?
                Assert.IsTrue(batch.CursorUpdatedTimestamp > 0);

                var firstResult = batch.SearchResults.First();

                //ensure file content was sent 
                Assert.NotNull(firstResult.JsonContent);
                Assert.IsNotEmpty(firstResult.JsonContent);

                Assert.IsTrue(firstResult.FileType == uploadContext.FileMetadata.AppData.FileType);
                Assert.IsTrue(firstResult.DataType == uploadContext.FileMetadata.AppData.DataType);
                Assert.IsTrue(firstResult.UserDate == uploadContext.FileMetadata.AppData.UserDate);
                Assert.IsTrue(firstResult.ContentType == uploadContext.FileMetadata.ContentType);
                Assert.IsTrue(string.IsNullOrEmpty(firstResult.SenderDotYouId));

                //must be ordered correctly
                //TODO: How to test this with a fileId?
            }
        }

        [Test]
        public async Task CanQueryDriveRecentItemsRedactedContent()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            var uploadContext = await this.UploadFile(identity, tag, SecurityGroupType.Anonymous);

            using (var client = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var svc = RestService.For<IDriveTestHttpClientForYouAuth>(client);

                var startCursor = Array.Empty<byte>();
                var stopCursor = Array.Empty<byte>();
                var qp = new QueryParams();
                var resultOptions = new ResultOptions()
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = false
                };

                var response = await svc.GetBatch(uploadContext.UploadedFile.TargetDrive, startCursor, stopCursor, qp, resultOptions);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;
                Assert.IsNotNull(batch);
                Assert.IsTrue(batch.SearchResults.All(item => string.IsNullOrEmpty(item.JsonContent)), "One or more items had content");
            }
        }

        private async Task<UploadTestUtilsContext> UploadFile(DotYouIdentity identity, Guid tag, SecurityGroupType requiredSecurityGroup)
        {
            List<Guid> tags = new List<Guid>() { tag };

            var uploadFileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                PayloadIsEncrypted = false,
                AppData = new()
                {
                    ContentIsComplete = false,
                    JsonContent = JsonConvert.SerializeObject(new { message = "We're going to the beach; this is encrypted by the app" }),
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
                    OverwriteFileId = overwriteFileId,
                    ExpiresTimestamp = null
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
                    JsonContent = JsonConvert.SerializeObject(new { message = "We're going to the beach; this is encrypted by the app" }),
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

            return await _scaffold.OwnerApi.UploadFile(identity, instructionSet, uploadFileMetadata, payload);
        }
    }
}