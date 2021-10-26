using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Youverse.Core.Identity;
using Youverse.Core.SystemStorage;

namespace Youverse.Core.Tests.SystemStorageTests
{
    public class SystemStorageTests
    {
        DotYouIdentity frodo = new DotYouIdentity("frodobaggins.me");
        private readonly ILogger _logger =  NullLogger.Instance;
            
        [SetUp]
        public void Setup()
        {
        }

        [Test(Description = "Can store an object by Id and retrieve it by Id")]
        public void CanStoreObject()
        {
            //IStorage<TestClass> = new LiteDBSingleCollectionStorage<TestClass>(_logger, path)
        }


        [Test(Description = "Can create a indexed field that is not the ID and retrieve the object")]
        public void CanGetObjectByIndex()
        {
        }
        
    }
}