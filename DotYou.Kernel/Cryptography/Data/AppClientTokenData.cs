
using DotYou.Kernel.Cryptography;

namespace DotYou.Kernel.Services.Admin.Authentication
{
    // This token (table) maps between a client's token cookie 
    // and the AppTokenData table. E.g. imagine two chat clients
    // each with their own cookie, sharedSecret, accessing the same
    // chat 'app' (AppTokenData).
    //
    public class AppClientTokenData
    {
        public byte[] tokenId;       // Random 16-byte secure HTTP only client cookie
        public byte[] applicationId; // 16-byte guid id to lookup the Application entry
        public byte[] halfAkek;      // Random 16-byte client cookie needed to calculate the application KeK
        public byte[] SharedSecret;  // The secret shared with the client. We need one per client
        public NonceTable NonceKeeper { get; set; }
    }
}
