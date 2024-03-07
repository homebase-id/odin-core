using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Time;

namespace Odin.Hosting.Tests.AppAPI.Drive
{
    public class DriveQueryAppTests
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
                DisconnectIdentitiesAfterTransfer = true,
            };

            var uploadContext = await _scaffold.AppApi.CreateAppAndUploadFileMetadata(identity, uploadFileMetadata, options);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(uploadContext.TestAppContext);
            {
                var svc = _scaffold.RestServiceFor<IDriveTestHttpClientForApps>(client, uploadContext.TestAppContext.SharedSecret);
                var request = new QueryBatchRequest()
                {
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = uploadContext.TestAppContext.TargetDrive,
                        TagsMatchAtLeastOne = tags
                    },

                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                    {
                        CursorState = "",
                        MaxRecords = 10,
                        IncludeMetadataHeader = false
                    }
                };

                var response = await svc.GetBatch(request);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;

                Assert.IsNotNull(batch);
                Assert.IsNotNull(batch.SearchResults.Single(item => item.FileMetadata.AppData.Tags.Any(t => t == tag)));
            }
        }

        [Test]
        public async Task CanQueryBatchByArchivalStatus()
        {
            var identity = TestIdentities.Samwise;

            const int archivalStatus = 1;
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
                    ArchivalStatus = archivalStatus
                }
            };

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = true,
            };

            var uploadContext = await _scaffold.AppApi.CreateAppAndUploadFileMetadata(identity, uploadFileMetadata, options);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(uploadContext.TestAppContext);
            {
                var svc = _scaffold.RestServiceFor<IDriveTestHttpClientForApps>(client, uploadContext.TestAppContext.SharedSecret);
                var request = new QueryBatchRequest()
                {
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = uploadContext.TestAppContext.TargetDrive,
                        ArchivalStatus = new List<int>() { archivalStatus }
                    },

                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                    {
                        CursorState = "",
                        MaxRecords = 10,
                        IncludeMetadataHeader = false
                    }
                };

                var response = await svc.GetBatch(request);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;

                Assert.IsNotNull(batch);
                Assert.IsNotNull(batch.SearchResults.Single(item => item.FileMetadata.AppData.ArchivalStatus == archivalStatus));
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
                DisconnectIdentitiesAfterTransfer = true,
            };

            var uploadContext = await _scaffold.AppApi.CreateAppAndUploadFileMetadata(identity, uploadFileMetadata, options);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(uploadContext.TestAppContext);
            {
                var svc = _scaffold.RestServiceFor<IDriveTestHttpClientForApps>(client, uploadContext.TestAppContext.SharedSecret);

                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.TestAppContext.TargetDrive,
                };

                var resultOptions = new QueryBatchResultOptionsRequest()
                {
                    CursorState = "",
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
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
        public async Task CanQueryDriveModifiedArchivedItems()
        {
            var identity = TestIdentities.Samwise;
            const int archivalStatus = 1;

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = true,
            };

            var uploadFileMetadata_not_archived = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new()
                {
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0),
                    ArchivalStatus = 0
                }
            };

            var notArchivedUploadContext = await _scaffold.AppApi.CreateAppAndUploadFileMetadata(identity, uploadFileMetadata_not_archived, options);

            var uploadFileMetadata_archived = new UploadFileMetadata()
            {
                AllowDistribution = false,
                IsEncrypted = false,
                AppData = new()
                {
                    Content = OdinSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" }),
                    FileType = 100,
                    DataType = 202,
                    UserDate = new UnixTimeUtc(0),
                    ArchivalStatus = archivalStatus
                }
            };

            var uploadContext = await _scaffold.AppApi.CreateAppAndUploadFileMetadata(identity, uploadFileMetadata_archived, options);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(uploadContext.TestAppContext);
            {
                var svc = _scaffold.RestServiceFor<IDriveTestHttpClientForApps>(client, uploadContext.TestAppContext.SharedSecret);

                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.TestAppContext.TargetDrive,
                    ArchivalStatus =  new List<int>() { archivalStatus }
                };

                var resultOptions = new QueryBatchResultOptionsRequest()
                {
                    CursorState = "",
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
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

                //TODO: what to test here?
                Assert.IsTrue(batch.SearchResults.Any());
                Assert.IsNotNull(batch.CursorState);
                Assert.IsNotEmpty(batch.CursorState);

                var theFileResult = batch.SearchResults.Single();

                //ensure file content was sent 
                Assert.NotNull(theFileResult.FileMetadata.AppData.Content);
                Assert.IsNotEmpty(theFileResult.FileMetadata.AppData.Content);

                Assert.IsTrue(theFileResult.FileMetadata.AppData.FileType == uploadFileMetadata_archived.AppData.FileType);
                Assert.IsTrue(theFileResult.FileMetadata.AppData.ArchivalStatus == uploadFileMetadata_archived.AppData.ArchivalStatus);
                Assert.IsTrue(theFileResult.FileMetadata.AppData.DataType == uploadFileMetadata_archived.AppData.DataType);
                Assert.IsTrue(theFileResult.FileMetadata.AppData.UserDate == uploadFileMetadata_archived.AppData.UserDate);
                Assert.IsTrue(string.IsNullOrEmpty(theFileResult.FileMetadata.SenderOdinId));

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
                DisconnectIdentitiesAfterTransfer = true,
            };

            var uploadContext = await _scaffold.AppApi.CreateAppAndUploadFileMetadata(identity, uploadFileMetadata, options);

            var client = _scaffold.AppApi.CreateAppApiHttpClient(uploadContext.TestAppContext);
            {
                var svc = _scaffold.RestServiceFor<IDriveTestHttpClientForApps>(client, uploadContext.TestAppContext.SharedSecret);
                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.TestAppContext.TargetDrive,
                };

                var resultOptions = new QueryBatchResultOptionsRequest()
                {
                    CursorState = "", MaxRecords = 10,
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

    }
}