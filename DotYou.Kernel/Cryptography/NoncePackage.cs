using System;
using DotYou.Types;

/// <summary>
/// Goals here are that:
///   * the password never leaves the clients.
///   * the password hash changes with every login request, making playback impossible
///   * the private encryption key on the server is encrypted with a KEK
///   * the KEK is only given by the client to the server once when creating a user / changing password / logging in
///   * all sessions contain server and client data that when merged results in a KEK (using XOR for speed, maybe reconsider)
/// </summary>
namespace DotYou.Kernel.Cryptography
{
    public sealed class NoncePackage
    {
        public string SaltPassword64 { get; }
        public string SaltKek64 { get; }

        public string Nonce64 { get; }

        public NoncePackage(byte[] saltPassword, byte[] saltKek)
        {
            // Guard.Argument(saltPassword, nameof(saltPassword)).NotEmpty().Require(x => x.Length == IdentityKeySecurity.SALT_SIZE);
            // Guard.Argument(saltKek, nameof(saltKek)).NotEmpty().Require(x => x.Length == IdentityKeySecurity.SALT_SIZE);

            Nonce64 = Convert.ToBase64String(YFByteArray.GetRndByteArray(IdentityKeySecurity.SALT_SIZE));
            SaltPassword64 = Convert.ToBase64String(saltPassword);
            SaltKek64 = Convert.ToBase64String(saltKek);
        }
    }
}