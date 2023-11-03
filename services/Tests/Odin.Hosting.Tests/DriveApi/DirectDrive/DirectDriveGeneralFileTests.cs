using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;

namespace Odin.Hosting.Tests.DriveApi.DirectDrive;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDriveGeneralFileTests
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
    public async Task CanUpdateMetadataDataWithOutThumbnailsAndWithoutPayloads()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        var response = await client.DriveRedux.UploadNewMetadata(targetDrive.TargetDriveInfo, uploadedFileMetadata);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // get the file header
        var getHeaderResponse = await client.DriveRedux.GetFileHeader(uploadResult.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        Assert.IsNotNull(header);
        Assert.IsTrue(header.FileMetadata.AppData.Content == uploadedFileMetadata.AppData.Content);
        Assert.IsFalse(header.FileMetadata.Payloads.Any());
    }

    [Test]
    public async Task CanUploadFileWith2ThumbnailsAnd2Payloads()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        // create a drive
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        var testPayloads = new List<TestPayloadDefinition>()
        {
            new()
            {
                Key = "test_key_1",
                ContentType = "text/plain",
                Content = "some content for payload key 1".ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
                {
                    new ThumbnailContent()
                    {
                        PixelHeight = 200,
                        PixelWidth = 200,
                        ContentType = "image/png",
                        Content = TestMedia.ThumbnailBytes200,
                    }
                }
            },
            new()
            {
                Key = "test_key_2",
                ContentType = "text/plain",
                Content = "other types of content for key 2".ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
                {
                    new ThumbnailContent()
                    {
                        PixelHeight = 400,
                        PixelWidth = 400,
                        ContentType = "image/png",
                        Content = TestMedia.ThumbnailBytes400,
                    }
                }
            }
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await client.DriveRedux.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // get the file header
        var getHeaderResponse = await client.DriveRedux.GetFileHeader(uploadResult.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        Assert.IsNotNull(header);
        Assert.IsTrue(header.FileMetadata.AppData.Content == uploadedFileMetadata.AppData.Content);
        Assert.IsTrue(header.FileMetadata.Payloads.Count() == testPayloads.Count);
        
        //test the headers payload info
        foreach (var testPayload in testPayloads)
        {
            var payload = header.FileMetadata.Payloads.Single(p => p.Key == testPayload.Key);
            Assert.IsTrue(testPayload.Thumbnails.Count == payload.Thumbnails.Count);
            Assert.IsTrue(testPayload.ContentType == payload.ContentType);
            //Assert.IsTrue(payload.LastModified); //TODO: how to test?
        }

        // Get the payloads
        foreach (var definition in testPayloads)
        {
            var getPayloadResponse = await client.DriveRedux.GetPayload(uploadResult.File, definition.Key);
            Assert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
            Assert.IsTrue(getPayloadResponse.ContentHeaders!.LastModified.HasValue);
            Assert.IsTrue(getPayloadResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

            var content = (await getPayloadResponse.Content.ReadAsStreamAsync()).ToByteArray();
            CollectionAssert.AreEqual(content, definition.Content);

            // Check all the thumbnails
            foreach (var thumbnail in definition.Thumbnails)
            {
                var getThumbnailResponse = await client.DriveRedux.GetThumbnail(uploadResult.File,
                    thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);

                Assert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

                var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(thumbContent, thumbnail.Content);
            }
        }
    }

    [Test]
    public async Task DeletingFileDeletesAllPayloadsAndThumbnails()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        var testPayloads = new List<TestPayloadDefinition>()
        {
            new()
            {
                Key = "test_key_1",
                ContentType = "text/plain",
                Content = "some content for payload key 1".ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
                {
                    new ThumbnailContent()
                    {
                        PixelHeight = 200,
                        PixelWidth = 200,
                        ContentType = "image/png",
                        Content = TestMedia.ThumbnailBytes200,
                    }
                }
            },
            new()
            {
                Key = "test_key_2",
                ContentType = "text/plain",
                Content = "other types of content for key 2".ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
                {
                    new ThumbnailContent()
                    {
                        PixelHeight = 400,
                        PixelWidth = 400,
                        ContentType = "image/png",
                        Content = TestMedia.ThumbnailBytes400,
                    }
                }
            }
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await client.DriveRedux.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // get the file header
        var getHeaderResponse = await client.DriveRedux.GetFileHeader(uploadResult.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        Assert.IsNotNull(header);
        Assert.IsTrue(header.FileMetadata.AppData.Content == uploadedFileMetadata.AppData.Content);
        Assert.IsTrue(header.FileMetadata.Payloads.Count() == 2);
        
        //test the headers payload info
        foreach (var testPayload in testPayloads)
        {
            var payload = header.FileMetadata.Payloads.Single(p => p.Key == testPayload.Key);
            Assert.IsTrue(testPayload.Thumbnails.Count == payload.Thumbnails.Count);
            Assert.IsTrue(testPayload.ContentType == payload.ContentType);
            //Assert.IsTrue(payload.LastModified); //TODO: how to test?
        }

        // Get the payloads
        foreach (var definition in testPayloads)
        {
            var getPayloadResponse = await client.DriveRedux.GetPayload(uploadResult.File, definition.Key);
            Assert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
            Assert.IsTrue(getPayloadResponse.ContentHeaders!.LastModified.HasValue);
            Assert.IsTrue(getPayloadResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

            var content = (await getPayloadResponse.Content.ReadAsStreamAsync()).ToByteArray();
            CollectionAssert.AreEqual(content, definition.Content);

            foreach (var thumbnail in definition.Thumbnails)
            {
                var getThumbnailResponse =
                    await client.DriveRedux.GetThumbnail(uploadResult.File, thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);
                Assert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
                Assert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

                var thumbnailContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(thumbnailContent, thumbnail.Content);
            }
        }


        // Now that we know all are there, let's delete stuff

        var deleteFileResponse = await client.DriveRedux.DeleteFile(uploadResult.File);
        Assert.IsTrue(deleteFileResponse.IsSuccessStatusCode);
        var result = deleteFileResponse.Content;
        Assert.IsNotNull(result);

        Assert.IsTrue(result.LocalFileDeleted);
        Assert.IsFalse(result.RecipientStatus.Any());

// Get the payloads
        foreach (var definition in testPayloads)
        {
            var getPayloadResponse = await client.DriveRedux.GetPayload(uploadResult.File, definition.Key);
            Assert.IsTrue(getPayloadResponse.StatusCode == HttpStatusCode.NotFound);

            foreach (var thumbnail in definition.Thumbnails)
            {
                var getThumbnailResponse =
                    await client.DriveRedux.GetThumbnail(uploadResult.File, thumbnail.PixelWidth, thumbnail.PixelHeight, definition.Key);
                Assert.IsTrue(getThumbnailResponse.StatusCode == HttpStatusCode.NotFound);
            }
        }
    }


    [Test]
    public async Task InvalidPayloadKeyReturns404()
    {
        var client = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);
        // create a drive
        var targetDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Test Drive 001", "", allowAnonymousReads: true, false, false);

        // upload metadata
        var uploadedFileMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 100
            },

            AccessControlList = AccessControlList.OwnerOnly
        };

        var testPayloads = new List<TestPayloadDefinition>()
        {
            new()
            {
                Key = "test_key_1",
                ContentType = "text/plain",
                Content = "some content for payload key 1".ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
                {
                    new ThumbnailContent()
                    {
                        PixelHeight = 200,
                        PixelWidth = 200,
                        ContentType = "image/png",
                        Content = TestMedia.ThumbnailBytes200,
                    }
                }
            },
            new()
            {
                Key = "test_key_2",
                ContentType = "text/plain",
                Content = "other types of content for key 2".ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
                {
                    new ThumbnailContent()
                    {
                        PixelHeight = 400,
                        PixelWidth = 400,
                        ContentType = "image/png",
                        Content = TestMedia.ThumbnailBytes400,
                    }
                }
            }
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var response = await client.DriveRedux.UploadNewFile(targetDrive.TargetDriveInfo, uploadedFileMetadata, uploadManifest, testPayloads);

        Assert.IsTrue(response.IsSuccessStatusCode);
        var uploadResult = response.Content;
        Assert.IsNotNull(uploadResult);

        // get the file header
        var getHeaderResponse = await client.DriveRedux.GetFileHeader(uploadResult.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        Assert.IsNotNull(header);
        Assert.IsTrue(header.FileMetadata.GetPayloadDescriptor(WebScaffold.PAYLOAD_KEY).Thumbnails.Count() == 2);
        Assert.IsTrue(header.FileMetadata.Payloads.Count() == 2);

        // now that we know we have a valid file with a few payloads
        var getRandomPayload = await client.DriveRedux.GetPayload(uploadResult.File, "r3nd0m");
        Assert.IsTrue(getRandomPayload.StatusCode == HttpStatusCode.NotFound, $"Status code was {getRandomPayload.StatusCode}");
    }
}