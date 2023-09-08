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
            var db = new KeyChainDatabase("");
            db.CreateDatabase();

            var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var ecc = new EccFullKeyData(pwd, 1);

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
            db.tblKeyChain.Insert(r);

            Assert.Pass();
        }

        [Test]
        public void Test2()
        {
            var db = new KeyChainDatabase("");

            var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var ecc = new EccFullKeyData(pwd, 1);

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
            db.tblKeyChain.Insert(r);

            try
            {
                db.tblKeyChain.Insert(r);
                Assert.Fail();
            }
            catch (Exception)
            {
                Assert.Pass();
            }
        }

        [Test]
        public void Test3()
        {
            var db = new KeyChainDatabase("");
            db.CreateDatabase();

            var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var ecc = new EccFullKeyData(pwd, 1);

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

            db.tblKeyChain.Insert(r);

            // Make sure we can read a record even if we're in the semaphore lock 

            using (db.CreateCommitUnitOfWork())
            {
                var r2 = db.tblKeyChain.GetOldest(r.identity);
            }

            Assert.Pass();
        }
    }
}