using DotYou.Kernel.Services.Admin.Authentication;
using System;

// We'll need something like this on the identity:
//     List<TokenClientAppplicationManager> tokenApplicationList;


namespace DotYou.Kernel.Cryptography
{
    public static class AppRegistrationManager
    {
        /// <summary>
        /// Get the Application Dek by means of the LoginKek master key
        /// </summary>
        /// <param name="token">The ApplicationTokenData</param>
        /// <param name="LoginKek">The master key LoginKek</param>
        /// <returns>The decrypted Application DeK</returns>
        public static SecureKey GetApplicationDekWithLogin(AppRegistrationData token, SecureKey LoginKeK)
        {
            var appDek = AesCbc.DecryptBytesFromBytes_Aes(token.encryptedDek, LoginKeK.GetKey(), token.iv);

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
        /// <param name="name">Friendly name, not used</param>
        /// <param name="LoginKeK">The master key which can later be used to retrieve the aDeK or aKeK</param>
        /// <returns>A new ApplicationTokenData object</returns>
        public static AppRegistrationData CreateApplication(SecureKey LoginKeK)
        {
            var appDEK = new SecureKey(YFByteArray.GetRndByteArray(16)); // Create the ApplicationDataEncryptionKey (AdeK)

            var token = new AppRegistrationData();

            (token.iv, token.encryptedDek) = AesCbc.EncryptBytesToBytes_Aes(appDEK.GetKey(), LoginKeK.GetKey());

            return token;
        }
    }
}
