using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Youverse.Core.Identity;
using Youverse.Core.Services.Profile;

namespace Youverse.Core.Services.Transit
{
    public interface IEncryptionService
    {
        /// <summary>
        /// Returns a new <see cref="KeyHeader"/> encrypted using <param name="publicKey"></param> and contents of the <param name="originalHeader"></param>
        /// </summary>
        /// <param name="originalHeader"></param>
        /// <param name="publicKey"></param>
        /// <returns>A KeyHeader encrypted using the  <param name="publicKey"></param>.
        Task<KeyHeader> Encrypt(KeyHeader originalHeader, byte[] publicKey);

        /// <summary>
        /// Encrypts the <param name="originalHeader">origin KeyHeader</param> using the <param name="recipientPublicKeys">batch</param>
        /// </summary>
        /// <returns>A dictionary of <see cref="KeyHeader"/>s by recipient</returns>
        Task<IDictionary<DotYouIdentity, KeyHeader>> Encrypt(KeyHeader originalHeader, IDictionary<DotYouIdentity, byte[]> recipientPublicKeys);
    }
}