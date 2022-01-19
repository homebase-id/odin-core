using NUnit.Framework;
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

            var applicationDek = appToken.DecryptKey(ref loginDek);

            Assert.Pass();
        }

    }
}