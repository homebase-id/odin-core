using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers;

namespace Youverse.Hosting.Tests.DriveApi.App
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
                }
            };

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = true,
            };

            var uploadContext = await _scaffold.AppApi.Upload(identity, uploadFileMetadata, options);

            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(identity, uploadContext.TestAppContext.ClientAuthenticationToken))
            {
                var svc = RestService.For<IDriveTestHttpClientForApps>(client);

                var request = new GetBatchRequest()
                {
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = uploadContext.TestAppContext.TargetDrive,
                        TagsMatchAtLeastOne = tags.Select(t => t.ToByteArray())
                    },

                    ResultOptions = new GetBatchQueryResultOptions()
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
                Assert.IsNotNull(batch.SearchResults.Single(item => item.Tags.Contains(tag)));
            }
        }

        [Test]
        public async Task CanQueryDriveRecentItems()
        {
            var identity = TestIdentities.Samwise;

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
                    UserDate = 0
                }
            };

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = true,
            };

            var uploadContext = await _scaffold.AppApi.Upload(identity, uploadFileMetadata, options);

            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(identity, uploadContext.TestAppContext.ClientAuthenticationToken))
            {
                var svc = RestService.For<IDriveTestHttpClientForApps>(client);

                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.TestAppContext.TargetDrive,
                };

                var resultOptions = new GetBatchQueryResultOptions()
                {
                    CursorState = "",
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
                };

                var request = new GetBatchRequest()
                {
                    QueryParams = qp,
                    ResultOptions = resultOptions
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
                Assert.NotNull(firstResult.JsonContent);
                Assert.IsNotEmpty(firstResult.JsonContent);

                Assert.IsTrue(firstResult.FileType == uploadFileMetadata.AppData.FileType);
                Assert.IsTrue(firstResult.DataType == uploadFileMetadata.AppData.DataType);
                Assert.IsTrue(firstResult.UserDate == uploadFileMetadata.AppData.UserDate);
                Assert.IsTrue(firstResult.ContentType == uploadFileMetadata.ContentType);
                Assert.IsTrue(string.IsNullOrEmpty(firstResult.SenderDotYouId));

                //must be ordered correctly
                //TODO: How to test this with a fileId?
            }
        }

        [Test]
        public async Task CanQueryDriveRecentItemsRedactedContent()
        {
            var identity = TestIdentities.Samwise;

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
                    UserDate = 0
                }
            };

            TransitTestUtilsOptions options = new TransitTestUtilsOptions()
            {
                PayloadData = "some payload data for good measure",
                ProcessOutbox = false,
                ProcessTransitBox = false,
                DisconnectIdentitiesAfterTransfer = true,
            };

            var uploadContext = await _scaffold.AppApi.Upload(identity, uploadFileMetadata, options);

            using (var client = _scaffold.AppApi.CreateAppApiHttpClient(identity, uploadContext.TestAppContext.ClientAuthenticationToken))
            {
                var svc = RestService.For<IDriveTestHttpClientForApps>(client);

                var qp = new FileQueryParams()
                {
                    TargetDrive = uploadContext.TestAppContext.TargetDrive,
                };

                var resultOptions = new GetBatchQueryResultOptions()
                {
                    CursorState = "", MaxRecords = 10,
                    IncludeMetadataHeader = false
                };

                var request = new GetBatchRequest()
                {
                    QueryParams = qp,
                    ResultOptions = resultOptions
                };

                var response = await svc.GetBatch(request);
                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var batch = response.Content;
                Assert.IsNotNull(batch);
                Assert.IsTrue(batch.SearchResults.Any(), "No items returned");
                Assert.IsTrue(batch.SearchResults.All(item => string.IsNullOrEmpty(item.JsonContent)), "One or more items had content");
            }
        }
    }
}