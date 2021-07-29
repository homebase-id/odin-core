using System;

//
// This is the mapping table between a client's cookies and the
// application tokens.
//
//    {token-id, application-id, half-akek, shared-secret}
//
// Here's the high level flow:
//
//     Client sends (HTTPS) request to server. 
//     Server get's the cookies 'Token' and 'Half'
//     Server looks up a ClientApplication entry by means of Token
//     Server calculates the Application Kek by means of Half
//     Server loads TokenApplicationData using the application-id from the table.
//     Server accesses the Application KeK by mean of Half
//     Server accesses the Application DeK by means of the Application KeK
//
// We'll need something like this on the identity:
//     List<TokenClientAppplicationManager> tokenClientApplicationList;
//

namespace DotYou.Kernel.Cryptography
{
    public class TokenClientApplicationData
    {
        public byte[] tokenId;       // Random 16-byte secure HTTP only client cookie
        public byte[] applicationId; // 16-byte guid id to lookup the Application entry
        public byte[] halfAkek;      // Random 16-byte client cookie needed to calculate the application KeK
        public byte[] SharedSecret;  // The secret shared with the client. We need one per client
        // We may need the NonceGrowingManager in here
    }

    public static class TokenClientAppplicationManager
    {
        // For each client that needs to connect to an application call this function
        //
        //    It creates the ClientApplication table entry.
        //    It creates the tokenId which is to be the client's cookie equivalent ID.
        //    It creates the cookie2 which is to be the client's secod cookie needed to decrypt the AKeK
        //    It encrypts the AKeK and one for the master login KeK. 
        //    It returns the second client cookie which is the halfAkek needed to get the Akek 
        //      and the table entry
        //

        public static (byte[], TokenClientApplicationData) CreateClientToken(byte[] ApplicationId, byte[] ApplicationKeK, byte[] sharedSecret = null)
        {
            var token = new TokenClientApplicationData
            {
                tokenId  = YFByteArray.GetRndByteArray(16),
                applicationId = ApplicationId
            };

            var cookie2 = YFByteArray.GetRndByteArray(16);
            token.halfAkek = XorManagement.XorEncrypt(ApplicationKeK, cookie2);

            if (sharedSecret == null)
                token.SharedSecret = YFByteArray.GetRndByteArray(16);
            else
                token.SharedSecret = sharedSecret;

            return (cookie2, token);
        }


        // The client cookie2 application ½ KeK and server's ½ application Kek will join to form 
        // the application KeK that will unlock the DeK.
        public static byte[] GetApplicationKek(TokenClientApplicationData clientToken, byte[] cookie2)
        {
            return XorManagement.XorEncrypt(clientToken.halfAkek, cookie2);
        }
    }
}
