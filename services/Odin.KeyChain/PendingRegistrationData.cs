using Odin.Core.Time;
using Odin.Core.Cryptography.Signatures;

namespace Odin.KeyChain
{
    public class PendingRegistrationData
    {
        public readonly UnixTimeUtc timestamp;
        public readonly SignedEnvelope envelope;
        public string previousHashBase64;

        public PendingRegistrationData(SignedEnvelope envelope, string previousHashBase64)
        {
            this.timestamp = UnixTimeUtc.Now();
            this.envelope = envelope;
            this.previousHashBase64 = previousHashBase64;
        }
    }


}
