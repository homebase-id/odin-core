using System;

namespace DotYou.Kernel.Services.Verification
{
    /// <summary>
    /// Used to ensure a message or communication can be verified using the <see cref="ISenderVerificationService"/>.
    /// </summary>
    public interface IVerifiable
    {
        /// <summary>
        /// The token used to look up the checksum during the verification process (i.e. this is a cache key)
        /// </summary>
        Guid GetToken();

        /// <summary>
        /// The checksum of the payload being verified
        /// </summary>
        string GetChecksum();
    }
}
