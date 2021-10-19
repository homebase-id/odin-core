using NUnit.Framework;

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
            var loginKek = new SecureKey(ByteArrayUtil.GetRndByteArray(16)); // Simulate pre-existing

            // Create a new application and link the first client to it

            // First create a new app. The new app will contain a new pair of
            // (app-kek,app-dek). Both are encrypted with the loginKek and
            // can later be retrieved with the loginKek.
            //
            var appToken = AppRegistrationManager.CreateAppKey(loginKek.GetKey());

            // Now create a mapping from a client device/app to the application token above

            var applicationDek = AppRegistrationManager.GetApplicationDekWithLogin(appToken, loginKek);

            Assert.Pass();
        }


        [Test]
        public void TokenBase2Pass()
        {
            // Pre-requisites
            var loginDek = new SecureKey(ByteArrayUtil.GetRndByteArray(16)); // Pre-existing

            // Create a new application and link the first client to it

            // First create a new app. The new app will contain a new pair of
            // (app-kek,app-dek). Both are encrypted with the loginKek and
            // can later be retrieved with the loginKek.
            //
            var appToken = AppRegistrationManager.CreateAppKey(loginDek.GetKey());

            // Now create a mapping from a client device/app to the application token above

            // First get the application DEK
            var appDekViaLogin = AppRegistrationManager.GetApplicationDekWithLogin(appToken, loginDek);

            // Now create the mapping
            // TODO: xxx the id needs to be removed
            var (halfKey, clientToken) = AppClientTokenManager.CreateClientToken(ByteArrayUtil.GetRndByteArray(16), appDekViaLogin.GetKey());

            // The two cookies / keys to give to the client are:
            //   clientToken.TokenId ["Token"]
            //   cookie2             ["Half"]

            var appDekViaCookies = AppClientTokenManager.GetApplicationDek(clientToken.halfAdek, halfKey);

            if (ByteArrayUtil.EquiByteArrayCompare(appDekViaLogin.GetKey(), appDekViaCookies.GetKey()) == false)
            {
                Assert.Fail();
                return;
            }

        }

    }
}
