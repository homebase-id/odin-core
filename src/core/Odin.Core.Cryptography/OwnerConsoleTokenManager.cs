using System;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Identity;

//
// After lots of thinking I strongly discourage expiring the login cookie(s).
// The only weak spot in our system is the password dialog, because the users
// tend to not look at the URL to make sure it's their own site. EvilGnix2 is
// the perfect example of what we need to protect against. 
//
// Therefore this will be much more effective IMO:
//   If we want to validate the password with e.g. a one month interval, then
//   once the month is up, we still log them in, we show their page with the
//   full name. And we show some 'secret' pictures that are only visible when
//   you're logged in. Something they're used to seeing when they're logged in.
//   In this context, we can ask them to re-validate their password. 
//   
// Thus, effectively, we make the 'new device' login screen only appear when it
// is truly a new device. It can have lots of warnings and arrows pointing up
// to the URL. Thus lowering the risk that the user will just blindly 
// enter their password because they get the same annoying dialog once+ per month.
//

namespace Odin.Core.Cryptography
{
    public static class OwnerConsoleTokenManager
    {
        /// <summary>
        /// For each (logged in) client that needs access call this function
        /// 
        ///    It creates a LoginTokenData object and returns the cookie 
        ///    and loginTokenData objects. 
        ///    The cookies to be stored on the client (HTTP ONLY, secure medium) are:
        ///       token.id (the index key)
        ///       halfCookie (half the loginKek)
        ///    The LoginTokenData object is to be stored on the server and retrievable 
        ///    via the index cookie as DB load key.
        /// </summary>
        /// <param name="issuedTo"></param>
        /// <param name="nonce"></param>
        /// <param name="reply"></param>
        /// <param name="listEcc"></param>
        public static (SensitiveByteArray clientToken, OwnerConsoleClientRegistration token) CreateToken(
            OdinId issuedTo,
            NonceData nonce, 
            PasswordReply reply,
            EccFullKeyListData listEcc)
        {
            var (hpwd64, kek64, sharedsecret64) = PasswordDataManager.ParsePasswordEccReply(reply, listEcc);

            const int ttlSeconds = 31 * 24 * 3600; // Tokens can be semi-permanent.

            var serverToken = new OwnerConsoleClientRegistration
            {
                Id = SequentialGuid.CreateGuid(),
                SharedSecret = Convert.FromBase64String(sharedsecret64),
                ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlSeconds,
                IssuedTo = issuedTo
            };

            var kek = new SensitiveByteArray(Convert.FromBase64String(kek64)); // TODO: using
            serverToken.TokenEncryptedKek = new SymmetricKeyEncryptedXor(kek, out var clientToken);
            kek.Wipe();

            return (clientToken, serverToken);
        }


        // The client cookie2 application ½ KeK and server's ½ application Kek will join to form 
        // the application KeK that will unlock the DeK.
        public static SensitiveByteArray GetMasterKey(OwnerConsoleClientRegistration loginClientRegistration, SensitiveByteArray halfCookie)
        {
            return loginClientRegistration.TokenEncryptedKek.DecryptKeyClone(halfCookie);
        }
    }
}