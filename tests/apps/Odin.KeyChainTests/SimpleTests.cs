using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.SQLite.KeyChainDatabase;

namespace Odin.KeyChainTests
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public async Task Test1()
        {
            using var db = new KeyChainDatabase("KeyChainTest001");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();

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
                await db.tblKeyChain.InsertAsync(myc, r);

                Assert.Pass();
            }
        }

        [Test]
        public async Task Test2()
        {
            using var db = new KeyChainDatabase("KeyChainTest002");

            using (var myc = db.CreateDisposableConnection())
            {
                var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
                var ecc = new EccFullKeyData(pwd, EccKeySize.P384, 1);

                await db.CreateDatabaseAsync();

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
                await db.tblKeyChain.InsertAsync(myc, r);

                try
                {
                    await db.tblKeyChain.InsertAsync(myc, r);
                    Assert.Fail();
                }
                catch (Exception)
                {
                    Assert.Pass();
                }
            }
        }

        [Test]
        public async Task Test3()
        {
            using var db = new KeyChainDatabase("KeyChainTest003");

            using (var myc = db.CreateDisposableConnection())
            {
                await db.CreateDatabaseAsync();

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

                await db.tblKeyChain.InsertAsync(myc, r);

                // Make sure we can read a record even if we're in the semaphore lock 

                await myc.CreateCommitUnitOfWorkAsync(async () =>
                {
                    var r2 = await db.tblKeyChain.GetOldestAsync(myc, r.identity);
                });

                Assert.Pass();
            }
        }
    }
}