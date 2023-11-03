using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Time;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using QueryModifiedRequest = Odin.Core.Services.Drives.QueryModifiedRequest;

namespace Odin.Hosting.Tests.OwnerApi.Drive.StandardFileSystem
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

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = true
            };

            var uploadContext = await _scaffold.OldOwnerApi.Upload(identity.OdinId, uploadFileMetadata, options);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
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

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = true
            };

            var uploadContext = await _scaffold.OldOwnerApi.Upload(identity.OdinId, uploadFileMetadata, options);


            //
            // make a change to the file we just uploaded
            //

            var instructionSet = UploadInstructionSet.WithTargetDrive(uploadContext.UploadedFile.TargetDrive);
            instructionSet.StorageOptions.OverwriteFileId = uploadContext.UploadedFile.FileId;
            uploadFileMetadata.VersionTag = uploadContext.UploadResult.NewVersionTag;

            uploadFileMetadata.AppData.DataType = 10844;
            var _ = await _scaffold.OldOwnerApi.UploadFile(identity.OdinId, instructionSet, uploadFileMetadata, "a new payload", true);

            //
            // query the data to see the changes
            //
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);

                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.UploadedFile.TargetDrive
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

                var response = await svc.GetModified(request);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;
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
        }

        [Test]
        public async Task CanQueryDriveModifiedItemsRedactedContent()
        {
            var identity = TestIdentities.Samwise;

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

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = true
            };

            var uploadContext = await _scaffold.OldOwnerApi.Upload(identity.OdinId, uploadFileMetadata, options);

            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
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
                Assert.IsTrue(batch.SearchResults.All(item => string.IsNullOrEmpty(item.FileMetadata.AppData.Content)), "One or more items had content");
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

            var file1InstructionSet = new UploadInstructionSet()
            {
                TransferIv = ByteArrayUtil.GetRndByteArray(16),
                StorageOptions = new()
                {
                    Drive = file1TargetDrive
                }
            };

            await _scaffold.OldOwnerApi.UploadFile(identity.OdinId, file1InstructionSet, file1Metadata, "file one payload");

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

            await _scaffold.OldOwnerApi.UploadFile(identity.OdinId, file2InstructionSet, file2Metadata, "file two payload");

            //
            // perform the batch search
            //
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
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
                        TargetDrive = file2TargetDrive,
                        TagsMatchAtLeastOne = new[] { file2Tag }
                    },
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                    {
                        MaxRecords = 10,
                        IncludeMetadataHeader = true
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

                Assert.IsTrue(file1Result.IncludeMetadataHeader == file1Section.ResultOptionsRequest.IncludeMetadataHeader);

                var file2Result = batch.Results.Single(x => x.Name == file2Section.Name);
                CollectionAssert.AreEquivalent(file2Metadata.AppData.Tags, file2Result.SearchResults.Single().FileMetadata.AppData.Tags);
                Assert.IsTrue(file2Result.IncludeMetadataHeader == file2Section.ResultOptionsRequest.IncludeMetadataHeader);
            }
        }

        [Test]
        public async Task FailToQueryBatchCollectionWhenQueryHasDuplicateNames()
        {
            var identity = TestIdentities.Samwise;
            Guid file1Tag = Guid.NewGuid();

            TargetDrive file1TargetDrive = TargetDrive.NewTargetDrive();
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

            var file1InstructionSet = new UploadInstructionSet()
            {
                TransferIv = ByteArrayUtil.GetRndByteArray(16),
                StorageOptions = new()
                {
                    Drive = file1TargetDrive
                }
            };

            await _scaffold.OldOwnerApi.UploadFile(identity.OdinId, file1InstructionSet, file1Metadata, "file one payload");

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

            await _scaffold.OldOwnerApi.UploadFile(identity.OdinId, file2InstructionSet, file2Metadata, "file two payload");

            const string sectionName = "sectionName";
            //
            // perform the batch search
            //
            var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IDriveTestHttpClientForOwner>(client, ownerSharedSecret);

                var file1Section = new CollectionQueryParamSection()
                {
                    Name = sectionName,
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = file1TargetDrive,
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
                        TargetDrive = file2TargetDrive,
                        TagsMatchAtLeastOne = new[] { file2Tag }
                    },
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                    {
                        MaxRecords = 10,
                        IncludeMetadataHeader = true
                    }
                };


                var response = await svc.GetBatchCollection(new QueryBatchCollectionRequest()
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
}