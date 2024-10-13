using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Storage.SQLite.NotaryDatabase;
using Odin.Core.Time;

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
            using var db = new NotaryDatabase("NotariusTest001");
            using (var myc = db.CreateDisposableConnection())
            {
                db.CreateDatabase();

                var pwd = ByteArrayUtil.GetRndByteArray(16).ToSensitiveByteArray();
                var ecc = new EccFullKeyData(pwd, EccKeySize.P384, 1);

                var hash = ByteArrayUtil.CalculateSHA256Hash("odin".ToUtf8ByteArray());
                var key = ByteArrayUtil.CalculateSHA256Hash("someRsaPublicKeyDEREncoded".ToUtf8ByteArray());
                var r = new NotaryChainRecord()
                {
                    previousHash = hash,
                    identity = "frodo.baggins.me",
                    signedPreviousHash = key,
                    algorithm = "ublah",
                    publicKeyJwkBase64Url = ecc.PublicKeyJwkBase64Url(),
                    recordHash = hash,
                    timestamp = UnixTimeUtcUnique.Now(),
                    notarySignature = Guid.Empty.ToByteArray()
                };
                db.tblNotaryChain.Insert(myc, r);

                Assert.Pass();
            }
        }
    }
}