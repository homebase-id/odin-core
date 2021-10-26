using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Youverse.Core.Identity;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Tests.SystemStorageTests
{
    public class SystemStorageTests
    {
        private readonly ILogger _logger = NullLogger.Instance;
        private string _rootPath;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _rootPath = Path.Combine(Path.DirectorySeparatorChar.ToString(), @"tmp", "testsdata", "dotyoudata", folder);

            Directory.Delete(_rootPath, true);
        }

        [SetUp]
        public void Setup()
        {
        }

        private string FixName(string name)
        {
            return name.Replace("<", "").Replace(">", "");
        }

        [Test(Description = "Can store an object by Id and retrieve it by Id")]
        public async Task CanStoreObject()
        {
            string collectionName = FixName(MethodBase.GetCurrentMethod().DeclaringType.Name);
            ;
            string folder = Path.Combine(_rootPath, collectionName);
            IStorage<TestClass> storage = new LiteDBSingleCollectionStorage<TestClass>(_logger, folder, collectionName);

            var item = new TestClass()
            {
                Id = Guid.NewGuid(),
                Title = "This IS a title of AN Item å Ø 平仮名, ひらがな"
            };

            await storage.Save(item);

            var savedItem = await storage.Get(item.Id);

            Assert.IsTrue(item.Title.Equals(savedItem.Title, StringComparison.Ordinal));
        }


        [Test(Description = "Can create a non-unique indexed field that is not the ID and retrieve the object")]
        public async Task CanGetObjectBySecondaryNonUniqueIndex()
        {
            string collectionName = FixName(MethodBase.GetCurrentMethod().DeclaringType.Name);
            string folder = Path.Combine(_rootPath, collectionName);
            IStorage<TestClass> storage = new LiteDBSingleCollectionStorage<TestClass>(_logger, folder, collectionName);

            await storage.EnsureIndex(key => key.Title, unique: false);
            const string title = "This IS a title of AN Item å Ø 平仮";

            var item1 = new TestClass()
            {
                Id = Guid.NewGuid(),
                Title = title
            };

            await storage.Save(item1);
            var savedItem = await storage.FindOne(p => p.Title == item1.Title);
            Assert.IsTrue(item1.Title.Equals(savedItem.Title, StringComparison.Ordinal));

            //should not fail. 
            var item2 = new TestClass()
            {
                Id = Guid.NewGuid(),
                Title = title
            };

            // ReSharper disable once AsyncVoidLambda
            Assert.DoesNotThrowAsync(() => storage.Save(item2));

            var shouldHaveTwo = await storage.Find(p => p.Title == item1.Title, PageOptions.Default);
            Assert.IsTrue(shouldHaveTwo.Results.Count == 2);
        }

        [Test(Description = "Can create a UNIQUE indexed field that is not the ID and retrieve the object")]
        public async Task CanGetObjectBySecondaryUniqueIndex()
        {
            string collectionName = FixName(MethodBase.GetCurrentMethod().DeclaringType.Name);
            string folder = Path.Combine(_rootPath, collectionName);
            IStorage<TestClass> storage = new LiteDBSingleCollectionStorage<TestClass>(_logger, folder, collectionName);

            await storage.EnsureIndex(key => key.Title, unique: true);

            const string title = "This IS a title of AN Item å Ø 平仮";

            var item1 = new TestClass()
            {
                Id = Guid.NewGuid(),
                Title = title
            };

            await storage.Save(item1);

            var savedItem = await storage.FindOne(p => p.Title == item1.Title);
            Assert.IsTrue(item1.Title.Equals(savedItem.Title, StringComparison.Ordinal));

            //should fail. 
            var item2 = new TestClass()
            {
                Id = Guid.NewGuid(),
                Title = title
            };

            // ReSharper disable once AsyncVoidLambda
            Assert.ThrowsAsync<UniqueIndexException>(() => storage.Save(item2));

            //
            // try
            // {
            //     await storage.Save(item2);
            // }
            // catch (UniqueIndexException uex)
            // {
            //     Assert.Pass();
            // }

            //Assert.Fail();
        }
    }
}