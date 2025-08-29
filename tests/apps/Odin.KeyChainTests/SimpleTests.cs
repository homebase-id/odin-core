using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.Concurrency;
using Odin.Core.Storage.Database;
using Odin.Core.Storage.Database.KeyChain;
using Odin.Core.Storage.Database.KeyChain.Table;

namespace Odin.KeyChainTests
{
    public class Tests
    {
        private IContainer _container = null!;
        private KeyChainDatabase _db = null!;

        [SetUp]
        public void Setup()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging(builder => builder.AddConsole());
            serviceCollection.AddSingleton<INodeLock, NodeLock>();

            var builder = new ContainerBuilder();
            builder.Populate(serviceCollection);

            builder.AddDatabaseCounterServices();
            builder.AddSqliteKeyChainDatabaseServices(":memory:");

            _container = builder.Build();
            _db = _container.Resolve<KeyChainDatabase>();
        }

        [TearDown]
        public void TearDown()
        {
            _container.Dispose();
        }

        [Test]
        public async Task Test1()
        {
            await _db.MigrateDatabaseAsync();

            var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var ecc = new EccFullKeyData(pwd, EccKeySize.P384, 1);

            var hash = ByteArrayUtil.CalculateSHA256Hash("odin".ToUtf8ByteArray());
            var key = ByteArrayUtil.CalculateSHA256Hash("someRsaPublicKeyDEREncoded".ToUtf8ByteArray());
            var r = new KeyChainRecord()
            {
                previousHash = hash,
                identity = "frodo.baggins.me",
                signedPreviousHash = key,
                algorithm = "ublah",
                publicKeyJwkBase64Url = ecc.PublicKeyJwkBase64Url(),
                recordHash = hash
            };
            await _db.KeyChain.InsertAsync(r);

            Assert.Pass();

        }

        [Test]
        public async Task Test2()
        {
            var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var ecc = new EccFullKeyData(pwd, EccKeySize.P384, 1);

            await _db.MigrateDatabaseAsync();

            var hash = ByteArrayUtil.CalculateSHA256Hash("odin".ToUtf8ByteArray());
            var key = ByteArrayUtil.CalculateSHA256Hash("someRsaPublicKeyDEREncoded".ToUtf8ByteArray());
            // var r = new BlockChainRecord() { identity = "frodo.baggins.me", recordHash = hash, publicKey = key, signedNonce = key };
            var r = new KeyChainRecord()
            {
                previousHash = hash,
                identity = "frodo.baggins.me",
                signedPreviousHash = key,
                algorithm = "ublah",
                publicKeyJwkBase64Url = ecc.PublicKeyJwkBase64Url(),
                recordHash = hash
            };
            await _db.KeyChain.InsertAsync(r);

            try
            {
                await _db.KeyChain.InsertAsync(r);
                Assert.Fail();
            }
            catch (Exception)
            {
                Assert.Pass();
            }

        }

        [Test]
        public async Task Test3()
        {
            await _db.MigrateDatabaseAsync();

            var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var ecc = new EccFullKeyData(pwd, EccKeySize.P384, 1);

            var hash = ByteArrayUtil.CalculateSHA256Hash("odin".ToUtf8ByteArray());
            var key = ByteArrayUtil.CalculateSHA256Hash("someRsaPublicKeyDEREncoded".ToUtf8ByteArray());
            var r = new KeyChainRecord()
            {
                previousHash = hash,
                identity = "frodo.baggins.me",
                signedPreviousHash = key,
                algorithm = "ublah",
                publicKeyJwkBase64Url = ecc.PublicKeyJwkBase64Url(),
                recordHash = hash
            };

            await _db.KeyChain.InsertAsync(r);

            Assert.Pass();
        }
    }
}