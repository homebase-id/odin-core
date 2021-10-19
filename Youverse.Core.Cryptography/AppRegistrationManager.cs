using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;

// We'll need something like this on the identity:
//     List<TokenClientAppplicationManager> tokenApplicationList;


namespace Youverse.Core.Cryptography
{
    public static class AppRegistrationManager
    {
        /// <summary>
        /// Get the Application Dek by means of the LoginKek master key
        /// </summary>
        /// <param name="token">The ApplicationTokenData</param>
        /// <param name="loginDek">The master key LoginKek</param>
        /// <returns>The decrypted Application DeK</returns>
        public static SecureKey GetApplicationDekWithLogin(AppEncryptionKey token, SecureKey loginDek)
        {
            var appDek = AesCbc.DecryptBytesFromBytes_Aes(token.EncryptedAppDeK, loginDek.GetKey(), token.AppIV);

            return new SecureKey(appDek);
        }
        

        // On creating a new application, e.g. 'chat' this is done only once:
        //
        //    It creates the application ID which is to be the first client cookie
        //    It creates the application specific AKeK and stores it XOR'ed with the ID
        //    It encrypts the AKeK and one for the master login KeK. 
        //    It returns the second client cookie which is the halfAkek needed to get the Akek.
        //
        // Remember you can only add a new applicaiton with a confirmation process
        // which will be a web based dialogue. So we will always have access to the
        // Login KeK in this context.
        //

        /// <summary>
        /// Create a new application. This should only be done once for an application, e.g. chat.
        /// </summary>
        /// <param name="loginDek">The master key which can later be used to retrieve the appDeK</param>
        /// <returns>A new ApplicationTokenData object</returns>
        public static AppEncryptionKey CreateAppKey(byte[] loginDek)
        {
            var appDek = new SecureKey(ByteArrayUtil.GetRndByteArray(16)); // Create the ApplicationDataEncryptionKey (AdeK)
            
            var token = new AppEncryptionKey();

            (token.AppIV, token.EncryptedAppDeK) = AesCbc.EncryptBytesToBytes_Aes(appDek.GetKey(), loginDek);

            appDek.Wipe();
            
            return token;
        }
    }
}
