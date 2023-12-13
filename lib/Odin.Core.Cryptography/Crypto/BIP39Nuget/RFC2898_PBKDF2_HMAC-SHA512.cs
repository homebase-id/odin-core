using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Odin.Core;
using System;

namespace Bitcoin.BitcoinUtilities
{
	/// <summary>
	/// Implementation of the Rfc2898 PBKDF2 specification located here http://www.ietf.org/rfc/rfc2898.txt using HMACSHA512 but modified as opposed to PWDTKto match the BIP39 test vectors
	/// Using BouncyCastle for the HMAC-SHA512 instead of Microsoft implementation
	/// NOTE NOT IDENTICLE TO PWDTK (PWDTK is concatenating password and salt together before hashing the concatenated byte block, this is simply hashing the salt as what we are told to do in BIP39, yes the mnemonic sentence is provided as the hmac key)
	/// Created by thashiznets@yahoo.com.au
	/// v1.1.0.0
	/// Bitcoin:1ETQjMkR1NNh4jwLuN5LxY7bbip39HC9PUPSV
	/// </summary>
	public class Rfc2898_pbkdf2_hmacsha512
	{
		public const int CMinIterations = 2048;

		//Length of the Hash Digest Output - 512 bits - 64 bytes
		public const int hLen = 64;

		/// <summary>
		/// A static publicly exposed version of GetDerivedKeyBytes_PBKDF2_HMACSHA512 which matches the exact specification in Rfc2898 PBKDF2 using HMACSHA512
		/// </summary>
		/// <param name="P">Password passed as a Byte Array</param>
		/// <param name="S">Salt passed as a Byte Array</param>
		/// <param name="c">Iterations to perform the underlying PRF over</param>
		/// <param name="dkLen">Length of Bytes to return, an AES 256 key wold require 32 Bytes</param>
		/// <returns>Derived Key in Byte Array form ready for use by chosen encryption function</returns>
		public static Byte[] PBKDF2(Byte[] P, Byte[] S, int c = CMinIterations, int dkLen = hLen)
		{
			// MS Switched to standard BC and it works...
            var obj = KeyDerivation.Pbkdf2(
                P.ToStringFromUtf8Bytes(),
                S,
                KeyDerivationPrf.HMACSHA512,
                c,
                dkLen);

			return obj;
		}
	}
}