using DotYou.Kernel.Cryptography;
using System;

namespace DotYou.Kernel.Services.Admin.Authentication
{
    public class LoginTokenData: IDisposable
    {
        ~LoginTokenData()
        {
            this.Dispose();
        }

        public Guid Id { get; set; }

        /// <summary>
        /// Point in time the token expires
        /// </summary>
        public Int64 ExpiryUnixTime { get; set; }

        /// <summary>
        /// The Server's 1/2 of the KeK
        /// </summary>
        public byte[] HalfKey { get; set; }

        /// <summary>
        /// The shared secret between the client and the host
        /// </summary>
        public byte[] SharedSecret { get; set; }

        public NonceTable NonceKeeper { get; set; }

        public void Dispose()
        {
            YFByteArray.WipeByteArray(this.HalfKey);
            YFByteArray.WipeByteArray(this.SharedSecret);
        }
    }
}