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
            var appToken = new SymmetricKeyEncryptedAes(loginDek);

            // Now create a mapping from a client device/app to the application token above

            var applicationDek = appToken.DecryptKey(loginDek.GetKey());

            Assert.Pass();
        }


        [Test]
        public void TokenBase2Pass()
        {
            // Pre-requisites
            var loginDek = new SensitiveByteArray(ByteArrayUtil.GetRndByteArray(16)); // Pre-existing

            // Create a new application and link the first client to it

            // First create a new app. The new app will contain a new pair of
            // (app-kek,app-dek). Both are encrypted with the loginKek and
            // can later be retrieved with the loginKek.
            //
            var appToken = new SymmetricKeyEncryptedAes(loginDek); //simulate pre-existing

            // Now create a mapping from a client device/app to the application token above

            // First get the application DEK
            var appDekViaLogin = appToken.DecryptKey(loginDek.GetKey());

            // Now create the mapping
            // TODO: xxx the id needs to be removed
            var (clientToken, srvRegData) = AppClientTokenManager.CreateClientToken(appDekViaLogin, ByteArrayUtil.GetRndByteArray(16));

            // The two cookies / keys to give to the client are:
            //   clientToken.TokenId ["Token"]
            //   cookie2             ["Half"]

            var appDekViaCookies = srvRegData.keyHalfKek.DecryptKey(clientToken);

            Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(appDekViaLogin.GetKey(), appDekViaCookies.GetKey()), "DeK does not match"); 
        }
    }
}