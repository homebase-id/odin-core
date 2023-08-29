using Odin.Core.Time;
using Odin.Core.Cryptography.Signatures;
using Odin.Core.Identity;
using Odin.Core.Util;

namespace Odin.KeyChain
{
    public class PendingRegistrationData
    {
        public readonly UnixTimeUtc timestamp;
        public readonly SignedEnvelope envelope;
        public readonly AsciiDomainName requestor;
        public string previousHashBase64;
        public string requestorPublicKeyJwkBase64;

        public PendingRegistrationData(SignedEnvelope envelope, string previousHashBase64, AsciiDomainName requestor, string requestorPublicKeyJwkBase64)
        {
            this.timestamp = UnixTimeUtc.Now();
            this.envelope = envelope;
            this.previousHashBase64 = previousHashBase64;
            this.requestor = requestor;
            this.requestorPublicKeyJwkBase64 = requestorPublicKeyJwkBase64;
        }
    }


}
