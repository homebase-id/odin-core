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
        public void Test1()
        {
            using var db = new KeyChainDatabase("");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();

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
                db.tblKeyChain.Insert(myc, r);

                Assert.Pass();
            }
        }

        [Test]
        public void Test2()
        {
            using var db = new KeyChainDatabase("");

            using (var myc = db.CreateDisposableConnection())
            {
                var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
                var ecc = new EccFullKeyData(pwd, EccKeySize.P384, 1);

                db.CreateDatabase();

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
                db.tblKeyChain.Insert(myc, r);

                try
                {
                    db.tblKeyChain.Insert(myc, r);
                    Assert.Fail();
                }
                catch (Exception)
                {
                    Assert.Pass();
                }
            }
        }

        [Test]
        public void Test3()
        {
            using var db = new KeyChainDatabase("");

            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();

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

                db.tblKeyChain.Insert(myc, r);

                // Make sure we can read a record even if we're in the semaphore lock 

                myc.CreateCommitUnitOfWork(() =>
                {
                    var r2 = db.tblKeyChain.GetOldest(myc, r.identity);
                });

                Assert.Pass();
            }
        }
    }
}