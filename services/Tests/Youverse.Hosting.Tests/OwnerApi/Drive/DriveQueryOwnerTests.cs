using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers;

namespace Youverse.Hosting.Tests.OwnerApi.Drive
{
    public class DriveQueryOwnerTests
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

        // [Test]
        // public async Task FailsWhenNoValidIndex()
        // {
        //     Assert.Inconclusive("TODO");
        // }

        [Test]
        public async Task CanQueryBatchByOneTag()
        {
            var identity = TestIdentities.Samwise;
            Guid tag = Guid.NewGuid();
            List<Guid> tags = new List<Guid>() { tag };

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
                }
            };

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = true
            };

            var uploadContext = await _scaffold.OwnerApi.Upload(identity.DotYouId, uploadFileMetadata, options);

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);

                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.UploadedFile.TargetDrive,
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
                    UserDate = 0
                }
            };

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = true
            };

            var uploadContext = await _scaffold.OwnerApi.Upload(identity.DotYouId, uploadFileMetadata, options);


            //
            // make a change to the file we just uploaded
            //

            var instructionSet = UploadInstructionSet.WithTargetDrive(uploadContext.UploadedFile.TargetDrive);
            instructionSet.StorageOptions.OverwriteFileId = uploadContext.UploadedFile.FileId;

            uploadFileMetadata.AppData.DataType = 10844;
            var _ = await _scaffold.OwnerApi.UploadFile(identity.DotYouId, instructionSet, uploadFileMetadata, "a new payload", true);

            //
            // query the data to see the changes
            //
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);

                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.UploadedFile.TargetDrive
                };

                var resultOptions = new QueryModifiedResultOptions()
                {
                    IncludeJsonContent = true,
                    Cursor = 0,
                    MaxRecords = 10,
                };

                var request = new QueryModifiedRequest()
                {
                    QueryParams = qp,
                    ResultOptions = resultOptions,
                };

                var response = await svc.GetModified(request);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;
                Assert.IsNotNull(batch);

                //TODO: what to test here?
                Assert.IsTrue(batch.SearchResults.Any());

                var firstResult = batch.SearchResults.First();

                //ensure file content was sent 
                Assert.NotNull(firstResult.FileMetadata.AppData.JsonContent);
                Assert.IsNotEmpty(firstResult.FileMetadata.AppData.JsonContent);

                Assert.IsTrue(firstResult.FileMetadata.AppData.FileType == uploadFileMetadata.AppData.FileType);
                Assert.IsTrue(firstResult.FileMetadata.AppData.DataType == uploadFileMetadata.AppData.DataType);
                Assert.IsTrue(firstResult.FileMetadata.AppData.UserDate == uploadFileMetadata.AppData.UserDate);
                Assert.IsTrue(firstResult.FileMetadata.ContentType == uploadFileMetadata.ContentType);
                Assert.IsTrue(string.IsNullOrEmpty(firstResult.FileMetadata.SenderDotYouId));

                //must be ordered correctly
                //TODO: How to test this with a fileId?
            }
        }

        [Test]
        public async Task CanQueryDriveModifiedItemsRedactedContent()
        {
            var identity = TestIdentities.Samwise;

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
                    UserDate = 0
                }
            };

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = true
            };

            var uploadContext = await _scaffold.OwnerApi.Upload(identity.DotYouId, uploadFileMetadata, options);

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);

                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.UploadedFile.TargetDrive
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

                var response = await svc.GetBatch(request);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;
                Assert.IsNotNull(batch);
                Assert.IsTrue(batch.SearchResults.Any(), "No items returned");
                Assert.IsTrue(batch.SearchResults.All(item => string.IsNullOrEmpty(item.FileMetadata.AppData.JsonContent)), "One or more items had content");
            }
        }

        [Test]
        public async Task CanQueryBatchCollectionAcrossDrives()
        {
            var identity = TestIdentities.Samwise;
            Guid file1Tag = Guid.NewGuid();

            TargetDrive file1TargetDrive = TargetDrive.NewTargetDrive();
            var file1Metadata = new UploadFileMetadata()
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
                    Tags = new List<Guid>() { file1Tag }
                }
            };

            var file1InstructionSet = new UploadInstructionSet()
            {
                TransferIv = ByteArrayUtil.GetRndByteArray(16),
                StorageOptions = new()
                {
                    Drive = file1TargetDrive
                }
            };

            await _scaffold.OwnerApi.UploadFile(identity.DotYouId, file1InstructionSet, file1Metadata, "file one payload");

            //
            // Add another drive and file
            //
            Guid file2Tag = Guid.NewGuid();
            TargetDrive file2TargetDrive = TargetDrive.NewTargetDrive();

            var file2InstructionSet = new UploadInstructionSet()
            {
                TransferIv = ByteArrayUtil.GetRndByteArray(16),
                StorageOptions = new()
                {
                    Drive = file2TargetDrive
                }
            };

            var file2Metadata = new UploadFileMetadata()
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
                    Tags = new List<Guid>() { file2Tag }
                }
            };

            await _scaffold.OwnerApi.UploadFile(identity.DotYouId, file2InstructionSet, file2Metadata, "file two payload");

            //
            // perform the batch search
            //
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);

                var file1Section = new CollectionQueryParamSection()
                {
                    Name = "file1Section",
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = file1TargetDrive,
                        TagsMatchAtLeastOne = new[] { file1Tag }
                    },
                    ResultOptions = new QueryBatchResultOptions()
                    {
                        MaxRecords = 10,
                        IncludeJsonContent = true
                    }
                };

                var file2Section = new CollectionQueryParamSection()
                {
                    Name = "file2Section",
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = file2TargetDrive,
                        TagsMatchAtLeastOne = new[] { file2Tag }
                    },
                    ResultOptions = new QueryBatchResultOptions()
                    {
                        MaxRecords = 10,
                        IncludeJsonContent = true
                    }
                };


                var response = await svc.GetBatchCollection(new QueryBatchCollectionRequest()
                {
                    Queries = new List<CollectionQueryParamSection>()
                    {
                        file1Section, file2Section
                    }
                });

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;

                Assert.IsNotNull(batch);
                Assert.IsTrue(batch.Results.Count == 2);

                var file1Result = batch.Results.Single(x => x.Name == file1Section.Name);
                CollectionAssert.AreEquivalent(file1Metadata.AppData.Tags, file1Result.SearchResults.Single().FileMetadata.AppData.Tags);

                Assert.IsTrue(file1Result.IncludeMetadataHeader == file1Section.ResultOptions.IncludeJsonContent);
                
                var file2Result = batch.Results.Single(x => x.Name == file2Section.Name);
                CollectionAssert.AreEquivalent(file2Metadata.AppData.Tags, file2Result.SearchResults.Single().FileMetadata.AppData.Tags);
                Assert.IsTrue(file2Result.IncludeMetadataHeader == file2Section.ResultOptions.IncludeJsonContent);
            }
        }
    }
}