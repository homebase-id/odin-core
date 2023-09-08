using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.SQLite.NotaryDatabase;

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
            var db = new NotaryDatabase("");
            db.CreateDatabase();

            var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
            var ecc = new EccFullKeyData(pwd, 1);

            var hash = ByteArrayUtil.CalculateSHA256Hash("odin".ToUtf8ByteArray());
            var key = ByteArrayUtil.CalculateSHA256Hash("someRsaPublicKeyDEREncoded".ToUtf8ByteArray());
            var r = new NotaryChainRecord()
            { 
                previousHash = hash, 
                identity = "frodo.baggins.me",
                signedPreviousHash = key,
                algorithm = "ublah",
                publicKeyJwkBase64Url = ecc.PublicKeyJwkBase64Url(),
                recordHash = hash
            };
            db.tblNotaryChain.Insert(r);

            Assert.Pass();
        }
    }
}