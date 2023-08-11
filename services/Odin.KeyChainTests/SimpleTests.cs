using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.SQLite.KeyChainDatabase;
using Odin.Core.Util;

namespace ChainTests
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
                nonce = Guid.NewGuid().ToByteArray(),
                signedNonce = key,
                algorithm = "ublah",
                publicKey = ecc.publicKey,
                recordHash = hash
            };
            db.tblBlockChain.Insert(r);

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
                nonce = Guid.NewGuid().ToByteArray(),
                signedNonce = key,
                algorithm = "ublah",
                publicKey = ecc.publicKey,
                recordHash = hash
            };
            db.tblBlockChain.Insert(r);

            try
            {
                db.tblBlockChain.Insert(r);
                Assert.Fail();
            }
            catch (Exception)
            {
                Assert.Pass();
            }
        }
    }
}