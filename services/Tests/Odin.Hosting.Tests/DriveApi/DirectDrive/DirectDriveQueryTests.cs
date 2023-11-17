using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Time;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using QueryModifiedRequest = Odin.Core.Services.Drives.QueryModifiedRequest;

namespace Odin.Hosting.Tests.DriveApi.DirectDrive
{
    public class DirectDriveQueryTests
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
        public async Task CanQueryBatchByOneTag()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            List<Guid> tags = new List<Guid>() { tag };

            var client = _scaffold.CreateOwnerApiClient(identity);
            var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

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
                }
            };

            var uploadResultResponse = await client.DriveRedux.UploadNewMetadata(targetDrive.TargetDriveInfo, uploadFileMetadata);
            var uploadResult = uploadResultResponse.Content;

            var qp = new FileQueryParams()
            {
                TargetDrive = targetDrive.TargetDriveInfo,
                TagsMatchAtLeastOne = tags
            };

            var resultOptions = new QueryBatchResultOptionsRequest()
            {
                CursorState = "",
                MaxRecords = 10,
                IncludeMetadataHeader = false
            };

            var request = new QueryBatchRequest()
            {
                QueryParams = qp,
                ResultOptionsRequest = resultOptions
            };

            var queryBatchResponse = await client.DriveRedux.QueryBatch(request);
            Assert.IsTrue(queryBatchResponse.IsSuccessStatusCode, $"Failed status code.  Value was {queryBatchResponse.StatusCode}");
            var batch = queryBatchResponse.Content;

            Assert.IsNotNull(batch);
            Assert.IsNotNull(batch.SearchResults.Single(item => item.FileMetadata.AppData.Tags.Any(t => t == tag)));
        }

        [Test]
        public async Task CanQueryDriveModifiedItems()
        {
            var identity = TestIdentities.Samwise;

            var client = _scaffold.CreateOwnerApiClient(identity);
            var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

            var uploadFileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new()
                {
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0)
                }
            };

            var uploadResultResponse = await client.DriveRedux.UploadNewMetadata(targetDrive.TargetDriveInfo, uploadFileMetadata);
            var uploadResult = uploadResultResponse.Content;

            //
            // Make a change to the file we just uploaded
            //

            uploadFileMetadata.AppData.DataType = 10844;
            var updateResult = await client.DriveRedux.UpdateExistingMetadata(uploadResult.File, uploadResult.NewVersionTag, uploadFileMetadata);
            Assert.IsTrue(updateResult.IsSuccessStatusCode);
            //
            // query the data to see the changes
            //

            var qp = new FileQueryParams()
            {
                TargetDrive = targetDrive.TargetDriveInfo
            };

            var resultOptions = new QueryModifiedResultOptions()
            {
                IncludeHeaderContent = true,
                Cursor = 0,
                MaxRecords = 10,
            };

            var request = new QueryModifiedRequest()
            {
                QueryParams = qp,
                ResultOptions = resultOptions,
            };

            var queryModifiedResponse = await client.DriveRedux.QueryModified(request);
            Assert.IsTrue(queryModifiedResponse.IsSuccessStatusCode, $"Failed status code.  Value was {queryModifiedResponse.StatusCode}");
            var batch = queryModifiedResponse.Content;
            Assert.IsNotNull(batch);

            //TODO: what to test here?
            Assert.IsTrue(batch.SearchResults.Any());

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

        [Test]
        public async Task CanQueryDriveModifiedItemsRedactedContent()
        {
            var identity = TestIdentities.Samwise;
            var client = _scaffold.CreateOwnerApiClient(identity);
            var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);


            var uploadFileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new()
                {
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0)
                }
            };

            var uploadResponse = await client.DriveRedux.UploadNewMetadata(targetDrive.TargetDriveInfo, uploadFileMetadata);


            var qp = new FileQueryParams()
            {
                TargetDrive = targetDrive.TargetDriveInfo
            };

            var resultOptions = new QueryBatchResultOptionsRequest()
            {
                CursorState = "",
                MaxRecords = 10,
                IncludeMetadataHeader = false
            };

            var request = new QueryBatchRequest()
            {
                QueryParams = qp,
                ResultOptionsRequest = resultOptions
            };

            var queryBatchResponse = await client.DriveRedux.QueryBatch(request);
            Assert.IsTrue(queryBatchResponse.IsSuccessStatusCode, $"Failed status code.  Value was {queryBatchResponse.StatusCode}");
            var batch = queryBatchResponse.Content;
            Assert.IsNotNull(batch);
            Assert.IsTrue(batch.SearchResults.Any(), "No items returned");
            Assert.IsTrue(batch.SearchResults.All(item => string.IsNullOrEmpty(item.FileMetadata.AppData.Content)), "One or more items had content");
        }

        [Test]
        public async Task CanQueryBatchCollectionAcrossDrives()
        {
            var identity = TestIdentities.Samwise;

            var client = _scaffold.CreateOwnerApiClient(identity);
            var file1TargetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

            Guid file1Tag = Guid.NewGuid();

            var file1Metadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new()
                {
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0),
                    Tags = new List<Guid>() { file1Tag }
                }
            };

            var uploadResponse1 = await client.DriveRedux.UploadNewMetadata(file1TargetDrive.TargetDriveInfo, file1Metadata);

            //
            // Add another drive and file
            //
            Guid file2Tag = Guid.NewGuid();
            var file2TargetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);


            var file2Metadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new()
                {
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0),
                    Tags = new List<Guid>() { file2Tag }
                }
            };

            var uploadResponse2 = await client.DriveRedux.UploadNewMetadata(file2TargetDrive.TargetDriveInfo, file2Metadata);

            //
            // perform the batch search
            //

            var file1Section = new CollectionQueryParamSection()
            {
                Name = "file1Section",
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = file1TargetDrive.TargetDriveInfo,
                    TagsMatchAtLeastOne = new[] { file1Tag }
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
                }
            };

            var file2Section = new CollectionQueryParamSection()
            {
                Name = "file2Section",
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = file2TargetDrive.TargetDriveInfo,
                    TagsMatchAtLeastOne = new[] { file2Tag }
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
                }
            };


            var queryBatchCollectionResponse = await client.DriveRedux.QueryBatchCollection(new QueryBatchCollectionRequest()
            {
                Queries = new List<CollectionQueryParamSection>()
                {
                    file1Section, file2Section
                }
            });

            Assert.IsTrue(queryBatchCollectionResponse.IsSuccessStatusCode, $"Failed status code.  Value was {queryBatchCollectionResponse.StatusCode}");
            var batch = queryBatchCollectionResponse.Content;

            Assert.IsNotNull(batch);
            Assert.IsTrue(batch.Results.Count == 2);

            var file1Result = batch.Results.Single(x => x.Name == file1Section.Name);
            CollectionAssert.AreEquivalent(file1Metadata.AppData.Tags, file1Result.SearchResults.Single().FileMetadata.AppData.Tags);

            Assert.IsTrue(file1Result.IncludeMetadataHeader == file1Section.ResultOptionsRequest.IncludeMetadataHeader);

            var file2Result = batch.Results.Single(x => x.Name == file2Section.Name);
            CollectionAssert.AreEquivalent(file2Metadata.AppData.Tags, file2Result.SearchResults.Single().FileMetadata.AppData.Tags);
            Assert.IsTrue(file2Result.IncludeMetadataHeader == file2Section.ResultOptionsRequest.IncludeMetadataHeader);
        }

        [Test]
        public async Task FailToQueryBatchCollectionWhenQueryHasDuplicateNames()
        {
            var identity = TestIdentities.Samwise;
            var client = _scaffold.CreateOwnerApiClient(identity);
            var file1TargetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

            Guid file1Tag = Guid.NewGuid();

            var file1Metadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new()
                {
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0),
                    Tags = new List<Guid>() { file1Tag }
                }
            };

            var uploadResponse1 = await client.DriveRedux.UploadNewMetadata(file1TargetDrive.TargetDriveInfo, file1Metadata);


            //
            // Add another drive and file
            //
            Guid file2Tag = Guid.NewGuid();
            var file2TargetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

            var file2Metadata = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new()
                {
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0),
                    Tags = new List<Guid>() { file2Tag }
                }
            };

            var uploadResponse2 = await client.DriveRedux.UploadNewMetadata(file2TargetDrive.TargetDriveInfo, file2Metadata);

            const string sectionName = "sectionName";
            //
            // perform the batch search
            //

            var file1Section = new CollectionQueryParamSection()
            {
                Name = sectionName,
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = file1TargetDrive.TargetDriveInfo,
                    TagsMatchAtLeastOne = new[] { file1Tag }
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
                }
            };

            var file2Section = new CollectionQueryParamSection()
            {
                Name = sectionName,
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = file2TargetDrive.TargetDriveInfo,
                    TagsMatchAtLeastOne = new[] { file2Tag }
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
                }
            };

            var response = await client.DriveRedux.QueryBatchCollection(new QueryBatchCollectionRequest()
            {
                Queries = new List<CollectionQueryParamSection>()
                {
                    file1Section, file2Section
                }
            });

            Assert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest, $"Status code was {response.StatusCode}");
        }
    }
}