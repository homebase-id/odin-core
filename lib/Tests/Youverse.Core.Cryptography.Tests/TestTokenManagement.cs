using NUnit.Framework;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Cryptography.Tests
{
    public class TestTokenManagement
    {
        [SetUp]
        public void Setup()
        {
        }


        [Test]
        public void TokenBasePass()
        {
            // Not really a test. Partially used this to hand-step through and manually
            // validate the encryption keys match up from creation to decryption.

            // Pre-requisites
            var loginDek = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16)); // Simulate pre-existing

            // Create a new application and link the first client to it

            // First create a new app. The new app will contain a new pair of
            // (app-kek,app-dek). Both are encrypted with the loginKek and
            // can later be retrieved with the loginKek.
            //
            var appToken = new SymmetricKeyEncryptedAes(ref loginDek);

            // Now create a mapping from a client device/app to the application token above

            var applicationDek = appToken.DecryptKeyClone(ref loginDek);

            Assert.Pass();
        }

        [Test]
        public void TokenXorPass()
        {
            var secretKey = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16)); // Simulate pre-existing
            var symKey = new SymmetricKeyEncryptedXor(ref secretKey, out var remoteHalfKey);
            var copyKey = symKey.DecryptKeyClone(ref remoteHalfKey);

            if (ByteArrayUtil.EquiByteArrayCompare(secretKey.GetKey(), copyKey.GetKey()))
                Assert.Pass();
            else
                Assert.Fail();
        }

    
        /// <summary>
        /// Connect Request example. Sam sends request to Frodo.
        /// </summary>
        [Test]
        public void TokenHosToHostShareKeyExampleALTERNATE()
        {
            var samsSharedSecretKey = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16)); // Sam create secret key
            var samsSymKey = new SymmetricKeyEncryptedXor(ref samsSharedSecretKey, out var samsRemoteHalfKey);

            // Remote half is for Frodo, symKey.KeyEncrypted is Sam's half
            // According to the protocol we send both halves to Frodo.

            var frodoLocalHalf = samsRemoteHalfKey;
            var frodoRemoteHalf = samsSymKey.KeyEncrypted.ToSensitiveByteArray();

            // Reverse construct Frodo's sym key - as suggested above, I think another constructor is better
            var frodoSymKey = new SymmetricKeyEncryptedXor(ref frodoLocalHalf, frodoRemoteHalf, false, false);
            var cloneFrodoSecretKey = frodoSymKey.DecryptKeyClone(ref frodoRemoteHalf);

            if (ByteArrayUtil.EquiByteArrayCompare(cloneFrodoSecretKey.GetKey(), samsSharedSecretKey.GetKey()) == false)
                Assert.Fail(); // Make sure we reverse constructed the key properly

            Assert.Pass();
        }
    }
}