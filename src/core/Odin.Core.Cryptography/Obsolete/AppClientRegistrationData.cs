using System;
using Odin.Core.Cryptography.Data;
using Odin.Core.Time;

namespace Odin.Core.Cryptography.Obsolete
{
    // This token (table) maps between a client's token cookie 
    // and the AppTokenData table. E.g. imagine two chat clients
    // each with their own cookie, sharedSecret, accessing the same
    // chat 'app' (AppTokenData). Each AppClientTokenData will have its
    // own App DEK by XOR'ing the cookie with the halfAdek.
    //
    [Obsolete]
    public class AppClientRegistrationData
    {
        public SymmetricKeyEncryptedXor DeviceEncryptedDeviceKey; // This is the server half of the key, client half is in the client cookie / token

        public byte[] SharedSecret;  // The secret shared with the client. We need one per client

        public NonceTable NonceKeeper { get; set; }

    }

    [Obsolete]
    public class DeviceRegistration
    {
        public Guid DeviceId { get; set; }

        public UnixTimeUtc RegistrationTimestamp { get; set; }

        public string DeviceName { get; set; }

        /// <summary>
        /// Specifies if and how this device supports multi-factor authentication
        /// </summary>
        public Guid MultiFactorAuthId { get; set; }
    }
}
