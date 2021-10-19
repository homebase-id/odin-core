using System;

namespace Youverse.Core.Cryptography.Data
{
    // This token (table) maps between a client's token cookie 
    // and the AppTokenData table. E.g. imagine two chat clients
    // each with their own cookie, sharedSecret, accessing the same
    // chat 'app' (AppTokenData). Each AppClientTokenData will have its
    // own App DEK by XOR'ing the cookie with the halfAdek.
    //
    public class AppClientRegistrationData
    {
        public Guid deviceId; // 16-byte guid id to lookup the Application entry
        public Guid applicationId; // 16-byte guid id to lookup the Application entry

        public Guid deviceApplicationId;      // Random 16-byte secure HTTP only client cookie
        public byte[] halfAdek;      // Random 16-byte client cookie needed to calculate the application DeK

        public byte[] SharedSecret;  // The secret shared with the client. We need one per client
        public NonceTable NonceKeeper { get; set; }
        public bool IsRevoked { get; set; }

    }

    public class DeviceRegistration
    {
        public Guid DeviceId { get; set; }
        
        public UInt64 RegistrationTimestamp { get; set; }

        public string DeviceName { get; set; }

        /// <summary>
        /// Specifies if and how this device supports multi-factor authentication
        /// </summary>
        public Guid MultiFactorAuthId { get; set; }
    }
}
