using DotYou.Types;
using System.Threading.Tasks;

namespace DotYou.Kernel.Services.Verification
{

    /// <summary>
    /// Handles verification the senders of incoming messages, invites, and other communications.
    /// </summary>
    public interface ISenderVerificationService
    {
        /// <summary>
        /// Adds <see cref="IVerifiable"/> to server memory for the specified <paramref name="ttlSeconds"/>.
        /// </summary>
        /// <param name="ttlSeconds">Number of seconds for the token to be valid from the time this method is called.</param>
        /// <returns></returns>
        void AddVerifiable(IVerifiable verifiable, int ttlSeconds = 10);

        /// <summary>
        /// Throws exception if the specifed token is invalid, expired, or non-existent.
        /// </summary>
        /// <param name="token">The token </param>
        /// <param name="checksum">A checksum value to be validated.  Useful to ensuring the validity of a message or other payload</param>
        /// <exception cref="VerificationFailedException">Thrown if the token is invalid</exception>
        /// <returns></returns>
        void AssertValidToken(VerificationPackage package);

        /// <summary>
        /// Calls the server for the <paramref name="dotYouId"/> to verfiy it did send the message.  Throws exception if the specifed token is invalid, expired, or non-existent.
        /// </summary>
        /// <param name="dotYouId">The sender identity to be verified</param>
        /// <param name="token">The token </param>
        /// <param name="checksum">A checksum value to be validated.  Useful to ensuring the validity of a message or other payload</param>
        /// <exception cref="VerificationFailedException">Thrown if the token is invalid</exception>
        /// <returns></returns>
        Task AssertTokenVerified(DotYouIdentity dotYouId, IVerifiable verifiable);

    }
}
