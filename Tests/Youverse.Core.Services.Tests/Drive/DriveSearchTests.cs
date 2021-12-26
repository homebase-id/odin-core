﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NuGet.Frameworks;
using NUnit.Framework;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query.LiteDb;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Tests.Drive
{
    [TestFixture]
    public class DriveSearchTests
    {
        private ServiceTestScaffold _scaffold;


        [SetUp]
        public void Setup()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new ServiceTestScaffold(folder);
            _scaffold.CreateContext();
            _scaffold.CreateSystemStorage();
            _scaffold.CreateLoggerFactory();
        }

        [TearDown]
        public void Cleanup()
        {
            //_scaffold.Cleanup();
        }

        [Test]
        public async Task CanSearchRecentFiles()
        {
            var driveService = new DriveService(_scaffold.Context, _scaffold.SystemStorage, _scaffold.LoggerFactory);
            var queryService = new DriveQueryService(driveService, null, _scaffold.LoggerFactory);

            const string driveName = "Test-Drive";
            var storageDrive = await driveService.CreateDrive(driveName);
            Assert.IsNotNull(storageDrive);

            var driveId = storageDrive.Id;
            Guid categoryId = Guid.Parse("531e832a-d3fa-4b37-b004-2e2b67a70971");

            await DriveTestUtils.AddFile(driveService, driveId, new TestFileProps()
            {
                MetadataJsonContent = new {message = "We're going to the beach"},
                PayloadContentType = "text/plain",
                PayloadData = "this is a text payload",
                CategoryId = categoryId
            });

            object file2MetadataContent = new {message = "some data"};
            await DriveTestUtils.AddFile(driveService, driveId, new TestFileProps()
            {
                MetadataJsonContent = file2MetadataContent,
                PayloadContentType = "application/json",
                PayloadData = JsonConvert.SerializeObject(new {data = "some data", number = 42}),
                CategoryId = categoryId
            });

            await DriveTestUtils.AddFile(driveService, driveId, new TestFileProps()
            {
                MetadataJsonContent = new {message = "more data goes here..."},
                ContentIsComplete = false,
                PayloadContentType = "application/json",
                PayloadData = JsonConvert.SerializeObject(new {data = "more data goes here and there and everywhere", number = 42}),
                CategoryId = null
            });

            //test the indexing
            var itemsByCategory = await queryService.GetItemsByCategory(driveId, categoryId, true, PageOptions.All);
            Assert.That(itemsByCategory.Results.Count, Is.EqualTo(2));
            Assert.IsNotNull(itemsByCategory.Results.SingleOrDefault(item => item.JsonContent == JsonConvert.SerializeObject(file2MetadataContent)));

            var recentItems = await queryService.GetRecentlyCreatedItems(driveId, true, PageOptions.All);
            Assert.That(recentItems.Results.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task CanQueryPartialIndex()
        {
            var driveService = new DriveService(_scaffold.Context, _scaffold.SystemStorage, _scaffold.LoggerFactory);
            var queryService = new DriveQueryService(driveService, null, _scaffold.LoggerFactory);

            const string driveName = "Test-Drive";
            var storageDrive = await driveService.CreateDrive(driveName);
            Assert.IsNotNull(storageDrive);

            var driveId = storageDrive.Id;
            Guid categoryId = Guid.Parse("531e832a-d3fa-4b37-b004-2e2b67a70971");

            await DriveTestUtils.AddFile(driveService, driveId, new TestFileProps()
            {
                MetadataJsonContent = new {message = "We're going to the beach"},
                PayloadContentType = "text/plain",
                PayloadData = "this is a text payload",
                CategoryId = categoryId
            });

            object file2MetadataContent = new {message = "some data"};
            await DriveTestUtils.AddFile(driveService, driveId, new TestFileProps()
            {
                MetadataJsonContent = file2MetadataContent,
                PayloadContentType = "application/json",
                PayloadData = JsonConvert.SerializeObject(new {data = "some data", number = 42}),
                CategoryId = categoryId
            });

            await DriveTestUtils.AddFile(driveService, driveId, new TestFileProps()
            {
                MetadataJsonContent = new {message = "more data goes here..."},
                ContentIsComplete = false,
                PayloadContentType = "application/json",
                PayloadData = JsonConvert.SerializeObject(new {data = "more data goes here and there and everywhere", number = 42}),
                CategoryId = null
            });

            //destroy the primary index
            string idxPath = Path.Combine(storageDrive.GetIndexPath());
            new DirectoryInfo(idxPath).Delete(true);

            //test the indexing
            var itemsByCategory = await queryService.GetItemsByCategory(driveId, categoryId, true, PageOptions.All);
            Assert.That(itemsByCategory.Results.Count, Is.EqualTo(2));
            Assert.IsNotNull(itemsByCategory.Results.SingleOrDefault(item => item.JsonContent == JsonConvert.SerializeObject(file2MetadataContent)));

            var recentItems = await queryService.GetRecentlyCreatedItems(driveId, true, PageOptions.All);
            Assert.That(recentItems.Results.Count, Is.EqualTo(3));
        }


        [Test]
        public async Task CanRebuildIndex()
        {
            var driveService = new DriveService(_scaffold.Context, _scaffold.SystemStorage, _scaffold.LoggerFactory);
            var queryService = new DriveQueryService(driveService, null, _scaffold.LoggerFactory);

            const string driveName = "Test-Drive";
            var storageDrive = await driveService.CreateDrive(driveName);
            Assert.IsNotNull(storageDrive);

            var driveId = storageDrive.Id;
            Guid categoryId = Guid.Parse("531e832a-d3fa-4b37-b004-2e2b67a70971");

            await DriveTestUtils.AddFile(driveService, driveId, new TestFileProps()
            {
                MetadataJsonContent = new {message = "We're going to the beach"},
                PayloadContentType = "text/plain",
                PayloadData = "this is a text payload",
                CategoryId = categoryId
            });

            object file2MetadataContent = new {message = "some data"};
            await DriveTestUtils.AddFile(driveService, driveId, new TestFileProps()
            {
                MetadataJsonContent = file2MetadataContent,
                PayloadContentType = "application/json",
                PayloadData = JsonConvert.SerializeObject(new {data = "some data", number = 42}),
                CategoryId = categoryId
            });

            await DriveTestUtils.AddFile(driveService, driveId, new TestFileProps()
            {
                MetadataJsonContent = new {message = "more data goes here..."},
                ContentIsComplete = false,
                PayloadContentType = "application/json",
                PayloadData = JsonConvert.SerializeObject(new {data = "more data goes here and there and everywhere", number = 42}),
                CategoryId = null
            });

            //test the indexing
            var itemsByCategory = await queryService.GetItemsByCategory(driveId, categoryId, true, PageOptions.All);
            Assert.That(itemsByCategory.Results.Count, Is.EqualTo(2));
            Assert.IsNotNull(itemsByCategory.Results.SingleOrDefault(item => item.JsonContent == JsonConvert.SerializeObject(file2MetadataContent)));

            var recentItems = await queryService.GetRecentlyCreatedItems(driveId, true, PageOptions.All);
            Assert.That(recentItems.Results.Count, Is.EqualTo(3));

            await queryService.RebuildBackupIndex(driveId);
            //
            // var itemsByCategoryRebuilt = await queryService.GetItemsByCategory(driveId, categoryId, true, PageOptions.All);
            // Assert.That(itemsByCategory.Results.Count, Is.EqualTo(2));
            // Assert.IsNotNull(itemsByCategoryRebuilt.Results.SingleOrDefault(item => item.JsonContent == JsonConvert.SerializeObject(file2MetadataContent)));
            //
            // var recentItemsRebuilt = await queryService.GetRecentlyCreatedItems(driveId, true, PageOptions.All);
            // Assert.That(recentItemsRebuilt.Results.Count, Is.EqualTo(3));

            _scaffold.LogDataPath();
        }
    }
}