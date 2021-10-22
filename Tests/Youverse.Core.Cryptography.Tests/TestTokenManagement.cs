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
            var loginDek = new SecureKey(ByteArrayUtil.GetRndByteArray(16)); // Simulate pre-existing

            // Create a new application and link the first client to it

            // First create a new app. The new app will contain a new pair of
            // (app-kek,app-dek). Both are encrypted with the loginKek and
            // can later be retrieved with the loginKek.
            //
            var appToken = AppRegistrationManager.CreateAppDek(loginDek.GetKey());

            // Now create a mapping from a client device/app to the application token above

            var applicationDek = AppRegistrationManager.DecryptAppDekWithLoginDek(appToken, loginDek);

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
            var appToken = AppRegistrationManager.CreateAppDek(loginDek.GetKey()); //simulate pre-existing

            // Now create a mapping from a client device/app to the application token above

            // First get the application DEK
            var appDekViaLogin = AppRegistrationManager.DecryptAppDekWithLoginDek(appToken, loginDek);

            // Now create the mapping
            // TODO: xxx the id needs to be removed
            var (serverToken, clientToken) = AppClientTokenManager.CreateClientToken(appDekViaLogin.GetKey(), ByteArrayUtil.GetRndByteArray(16));

            // The two cookies / keys to give to the client are:
            //   clientToken.TokenId ["Token"]
            //   cookie2             ["Half"]

            var appDekViaCookies = AppClientTokenManager.DecryptAppDekWithClientToken(clientToken.halfAdek, serverToken);

            Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(appDekViaLogin.GetKey(), appDekViaCookies.GetKey()), "DeK does not match"); 
        }
    }
}