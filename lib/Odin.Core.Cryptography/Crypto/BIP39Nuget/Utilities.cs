﻿using Odin.Core;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;


namespace Bitcoin.BitcoinUtilities
{
	/// <summary>
	/// A Library that provides common functionality between my other Bitcoin Modules
	/// Made by thashiznets@yahoo.com.au
	/// v1.0.0.2
	/// Bitcoin:1ETQjMkR1NNh4jwLuN5LxY7bMsHC9PUPSV
	/// </summary>  
	public static class Utilities
	{
		/// <summary>
		/// Calculates the SHA256 32 byte checksum of the input bytes
		/// </summary>
		/// <param name="input">bytes input to get checksum</param>
		/// <param name="offset">where to start calculating checksum</param>
		/// <param name="length">length of the input bytes to perform checksum on</param>
		/// <returns>32 byte array checksum</returns>
		public static byte[] Sha256Digest(byte[] input, int offset, int length)
		{
			var algorithm = new Sha256Digest();
			Byte[] firstHash = new Byte[algorithm.GetDigestSize()];
			algorithm.BlockUpdate(input, offset, length);
			algorithm.DoFinal(firstHash, 0);
			return firstHash;
		}

		/// <summary>
		/// Calculates the SHA512 64 byte checksum of the input bytes
		/// </summary>
		/// <param name="input">bytes input to get checksum</param>
		/// <param name="offset">where to start calculating checksum</param>
		/// <param name="length">length of the input bytes to perform checksum on</param>
		/// <returns>64 byte array checksum</returns>
		public static byte[] Sha512Digest(byte[] input, int offset, int length)
		{
			var algorithm = new Sha512Digest();
			Byte[] firstHash = new Byte[algorithm.GetDigestSize()];
			algorithm.BlockUpdate(input, offset, length);
			algorithm.DoFinal(firstHash, 0);
			return firstHash;
		}

		/// <summary>
		/// See <see cref="DoubleDigest(byte[], int, int)"/>.
		/// </summary>
		public static byte[] DoubleDigest(byte[] input)
		{
			return DoubleDigest(input, 0, input.Length);
		}

		/// <summary>
		/// Calculates the SHA-256 hash of the given byte range, and then hashes the resulting hash again. This is
		/// standard procedure in BitCoin. The resulting hash is in big endian form.
		/// </summary>
		public static byte[] DoubleDigest(byte[] input, int offset, int length)
		{
			var algorithm = new Sha256Digest();
			Byte[] firstHash = new Byte[algorithm.GetDigestSize()];
			algorithm.BlockUpdate(input, offset, length);
			algorithm.DoFinal(firstHash, 0);
			Byte[] secondHash = new Byte[algorithm.GetDigestSize()];
			algorithm.BlockUpdate(firstHash, 0, firstHash.Length);
			algorithm.DoFinal(secondHash, 0);
			return secondHash;
		}


		/// <summary>
		/// Converts a hex based string into its bytes contained in a byte array
		/// </summary>
		/// <param name="hex">The hex encoded string</param>
		/// <returns>the bytes derived from the hex encoded string</returns>
		public static byte[] HexStringToBytes(string hexString)
		{
			return Enumerable.Range(0, hexString.Length).Where(x => x % 2 == 0).Select(x => Convert.ToByte(hexString.Substring(x, 2), 16)).ToArray();
		}

		// <summary>
		/// Turns a byte array into a Hex encoded string
		/// </summary>
		/// <param name="bytes">The bytes to encode to hex</param>
		/// <returns>The hex encoded representation of the bytes</returns>
		public static string BytesToHexString(byte[] bytes, bool upperCase = false)
		{
			if (upperCase)
			{
				return string.Concat(bytes.Select(byteb => byteb.ToString("X2")).ToArray());
			}
			else
			{
				return string.Concat(bytes.Select(byteb => byteb.ToString("x2")).ToArray());
			}
		}

		/// <summary>
		/// Calculates the 64 byte checksum in accordance with HMAC-SHA512
		/// </summary>
		/// <param name="input">The bytes to derive the checksum from</param>
		/// <param name="offset">Where to start calculating checksum in the input bytes</param>
		/// <param name="length">Length of buytes to use to calculate checksum</param>
		/// <param name="hmacKey">HMAC Key used to generate the checksum (note differing HMAC Keys provide unique checksums)</param>
		/// <returns></returns>
		public static byte[] HmacSha512Digest(byte[] input, int offset, int length, byte[] hmacKey)
		{
			byte[] output = new byte[64];
			HMac _hmacsha512Obj;
			_hmacsha512Obj = new HMac(new Sha512Digest());
			ICipherParameters param = new Org.BouncyCastle.Crypto.Parameters.KeyParameter(hmacKey);
			_hmacsha512Obj.Init(param);
			_hmacsha512Obj.BlockUpdate(input, offset, length);
			_hmacsha512Obj.DoFinal(output, 0);
			return output;
		}

		/// <summary>
		/// Safely get Crypto Random byte array at the size you desire.
		/// </summary>
		/// <param name="size">Size of the crypto random byte array to build</param>
		/// <param name="seedStretchingIterations">Optional parameter to specify how many SHA512 passes occur over our seed before we use it. Higher value is greater security but uses more computational power. If random byte generation is taking too long try specifying values lower than the default of 5000. You can set 0 to turn off stretching</param>
		/// <returns>A byte array of completely random bytes</returns>
		public static byte[] GetRandomBytes(int size, int seedStretchingIterations = 5000)
		{
			//varies from system to system, a tiny amount of entropy, tiny
			int processorCount = System.Environment.ProcessorCount;

			//another tiny amount of entropy due to the varying nature of thread id
			int currentThreadId = System.Environment.CurrentManagedThreadId;

			//a GUID is considered unique so also provides some entropy
			byte[] guidBytes = Guid.NewGuid().ToByteArray();

            // REPLACED this combined with DateTime.Now is the default seed in BouncyCastles SecureRandom
            // REPLACED byte[] threadedSeedBytes = new Org.BouncyCastle.Crypto.Prng..ThreadedSeedGenerator().GenerateSeed(24, true);

			// New code
            SecureRandom secureRandom = new SecureRandom();
            byte[] threadedSeedBytes = new byte[24];
            secureRandom.NextBytes(threadedSeedBytes);

            byte[] output = new byte[size];

			//if for whatever reason it says 0 or less processors just make it 16
			if (processorCount <= 0)
			{
				processorCount = 16;
			}

			//if some fool trys to set stretching to < 0 we protect them from themselves
			if (seedStretchingIterations < 0)
			{
				seedStretchingIterations = 0;
			}

			//we create a SecureRandom based off SHA256 just to get a random int which will be used to determine what bytes to "take" from our built seed hash and then rehash those taken seed bytes using a KDF (key stretching) such that it would slow down anyone trying to rebuild private keys from common seeds.
			SecureRandom seedByteTakeDetermine = SecureRandom.GetInstance("SHA256PRNG");

			guidBytes = HmacSha512Digest(guidBytes, 0, guidBytes.Length, MergeByteArrays(threadedSeedBytes, UTF8Encoding.UTF8.GetBytes(Convert.ToString(System.Environment.TickCount))));

			try
			{
				seedByteTakeDetermine.SetSeed(((DateTime.Now.Ticks - System.Environment.TickCount) * processorCount) + currentThreadId);
				seedByteTakeDetermine.SetSeed(guidBytes);
				seedByteTakeDetermine.SetSeed(seedByteTakeDetermine.GenerateSeed(1 + currentThreadId));
				seedByteTakeDetermine.SetSeed(threadedSeedBytes);
			}
			catch
			{
				try
				{
					//if the number is too big or causes an error or whatever we will failover to this, as it's not our main source of random bytes and not used in the KDF stretching it's ok.
					seedByteTakeDetermine.SetSeed((DateTime.Now.Ticks - System.Environment.TickCount) + currentThreadId);
					seedByteTakeDetermine.SetSeed(guidBytes);
					seedByteTakeDetermine.SetSeed(seedByteTakeDetermine.GenerateSeed(1 + currentThreadId));
					seedByteTakeDetermine.SetSeed(threadedSeedBytes);
				}
				catch
				{
					//if again the number is too big or causes an error or whatever we will failover to this, as it's not our main source of random bytes and not used in the KDF stretching it's ok.
					seedByteTakeDetermine.SetSeed(DateTime.Now.Ticks - System.Environment.TickCount);
					seedByteTakeDetermine.SetSeed(guidBytes);
					seedByteTakeDetermine.SetSeed(seedByteTakeDetermine.GenerateSeed(1 + currentThreadId));
					seedByteTakeDetermine.SetSeed(threadedSeedBytes);
				}
			}

			//hardened seed
			byte[] toHashForSeed;

			try
			{
				toHashForSeed = BitConverter.GetBytes(((processorCount - seedByteTakeDetermine.Next(0, processorCount)) * System.Environment.TickCount) * currentThreadId);
			}
			catch
			{
				try
				{
					//if the number was too large or something we failover to this
					toHashForSeed = BitConverter.GetBytes(((processorCount - seedByteTakeDetermine.Next(0, processorCount)) + System.Environment.TickCount) * currentThreadId);
				}
				catch
				{
					//if the number was again too large or something we failover to this
					toHashForSeed = BitConverter.GetBytes(((processorCount - seedByteTakeDetermine.Next(0, processorCount)) + System.Environment.TickCount) + currentThreadId);
				}
			}

			toHashForSeed = Sha512Digest(toHashForSeed, 0, toHashForSeed.Length);
			toHashForSeed = MergeByteArrays(toHashForSeed, guidBytes);
			toHashForSeed = MergeByteArrays(toHashForSeed, BitConverter.GetBytes(currentThreadId));
			toHashForSeed = MergeByteArrays(toHashForSeed, BitConverter.GetBytes(DateTime.UtcNow.Ticks));
			toHashForSeed = MergeByteArrays(toHashForSeed, BitConverter.GetBytes(DateTime.Now.Ticks));
			toHashForSeed = MergeByteArrays(toHashForSeed, BitConverter.GetBytes(System.Environment.TickCount));
			toHashForSeed = MergeByteArrays(toHashForSeed, BitConverter.GetBytes(processorCount));
			toHashForSeed = MergeByteArrays(toHashForSeed, threadedSeedBytes);
			toHashForSeed = Sha512Digest(toHashForSeed, 0, toHashForSeed.Length);

			//we grab a random amount of bytes between 24 and 64 to rehash  make a new set of 64 bytes, using guidBytes as hmackey
			toHashForSeed = Sha512Digest(HmacSha512Digest(toHashForSeed, 0, seedByteTakeDetermine.Next(24, 64), guidBytes), 0, 64);

			seedByteTakeDetermine.SetSeed(currentThreadId + (DateTime.Now.Ticks - System.Environment.TickCount));

			//by making the iterations also random we are again making it hard to determin our seed by brute force
			int iterations = seedStretchingIterations - (seedByteTakeDetermine.Next(0, (seedStretchingIterations / seedByteTakeDetermine.Next(9, 100))));

			//here we use key stretching techniques to make it harder to replay the random seed values by forcing computational time up            
			byte[] seedMaterial = Rfc2898_pbkdf2_hmacsha512.PBKDF2(toHashForSeed, seedByteTakeDetermine.GenerateSeed(64), iterations);

			//build a SecureRandom object that uses Sha512 to provide randomness and we will give it our created above hardened seed
			SecureRandom secRand = new SecureRandom(new Org.BouncyCastle.Crypto.Prng.DigestRandomGenerator(new Sha512Digest()));

			//set the seed that we created just above
			secRand.SetSeed(seedMaterial);

			//generate more seed materisal            
			secRand.SetSeed(currentThreadId);
			secRand.SetSeed(MergeByteArrays(guidBytes, threadedSeedBytes));
			secRand.SetSeed(secRand.GenerateSeed(1 + secRand.Next(64)));

			//add our prefab seed again onto the previous material just to be sure the above statements are adding and not clobbering seed material
			secRand.SetSeed(seedMaterial);

			//here we derive our random bytes
			secRand.NextBytes(output, 0, size);

			return output;
		}

		/// <summary>
		/// Safely get Crypto Random byte array at the size you desire, made this async version because can take 500ms to complete and so this allows non-blocking for the 500ms.
		/// </summary>
		/// <param name="size">Size of the crypto random byte array to build</param>
		/// <param name="seedStretchingIterations">Optional parameter to specify how many SHA512 passes occur over our seed before we use it. Higher value is greater security but uses more computational power. If random byte generation is taking too long try specifying values lower than the default of 5000. You can set 0 to turn off stretching</param>
		/// <returns>A byte array of completely random bytes</returns>
		public async static Task<byte[]> GetRandomBytesAsync(int size, int seedStretchingIterations = 5000)
		{
			return await Task.Run<byte[]>(() => ByteArrayUtil.GetRndByteArray(size));
		}

		/// <summary>
		/// Merges two byte arrays
		/// </summary>
		/// <param name="source1">first byte array</param>
		/// <param name="source2">second byte array</param>
		/// <returns>A byte array which contains source1 bytes followed by source2 bytes</returns>
		public static Byte[] MergeByteArrays(Byte[] source1, Byte[] source2)
		{
			//Most efficient way to merge two arrays this according to http://stackoverflow.com/questions/415291/best-way-to-combine-two-or-more-byte-arrays-in-c-sharp
			Byte[] buffer = new Byte[source1.Length + source2.Length];
			System.Buffer.BlockCopy(source1, 0, buffer, 0, source1.Length);
			System.Buffer.BlockCopy(source2, 0, buffer, source1.Length, source2.Length);

			return buffer;
		}

		/// <summary>
		/// This switches the Endianess of the provided byte array, byte per byte we do bit swappy.
		/// </summary>
		/// <param name="bytes">Bytes to change endianess of</param>
		/// <returns>Bytes with endianess swapped</returns>
		public static byte[] SwapEndianBytes(byte[] bytes)
		{
			byte[] output = new byte[bytes.Length];

			int index = 0;

			foreach (byte b in bytes)
			{
				byte[] ba = { b };
				BitArray bits = new BitArray(ba);

				int newByte = 0;
				if (bits.Get(7)) newByte++;
				if (bits.Get(6)) newByte += 2;
				if (bits.Get(5)) newByte += 4;
				if (bits.Get(4)) newByte += 8;
				if (bits.Get(3)) newByte += 16;
				if (bits.Get(2)) newByte += 32;
				if (bits.Get(1)) newByte += 64;
				if (bits.Get(0)) newByte += 128;

				output[index] = Convert.ToByte(newByte);

				index++;
			}

			//I love lamp
			return output;
		}

		/// <summary>
		/// Returns a Positive BouncyCastle BigInteger
		/// </summary>
		/// <param name="bytes">Bytes to create BigInteger</param>
		/// <returns>A Positive BigInteger</returns>
		public static BigInteger NewPositiveBigInteger(byte[] bytes)
		{
			return new BigInteger(1, bytes);
		}

		/// <summary>
		/// Convert a .NET DateTime into a Unix Epoch represented time
		/// </summary>
		/// <param name="time">DateTime to convert</param>
		/// <returns>Number of ticks since the Unix Epoch</returns>
		public static ulong ToUnixTime(DateTime time)
		{
			return (ulong)(time.ToUniversalTime() - Globals.UnixEpoch).TotalSeconds;
		}


		/// <summary>
		/// Normalises a string with NKFD normal form
		/// </summary>
		/// <param name="toNormalise">String to be normalised</param>
		/// <returns>Normalised string</returns>
		public static String NormaliseStringNfkd(String toNormalise)
		{
			int bufferSize = NormalizeString(Globals.NORM_FORM.NormalizationKD, toNormalise, -1, null, 0);

			StringBuilder buffer = new StringBuilder(bufferSize);

			// Normalize.
			NormalizeString(Globals.NORM_FORM.NormalizationKD, toNormalise, -1, buffer, buffer.Capacity);

			// Check for and act on errors if you want.
			int error = Marshal.GetLastWin32Error();

			if (error != 0)
			{
				throw (new Exception("A Win32 error with code " + error + " has occured in unmanaged NormalizeString"));
			}
			char[] trim = { '\0' };

			return buffer.ToString().TrimEnd(trim);
		}

		/// <summary>
		/// Will be used internally for NFKD Normalisation
		/// </summary>
		/// <param name="NormForm">Normal Form to use</param>
		/// <param name="lpSrcString">Raw non-normalised source string</param>
		/// <param name="cwSrcLength">Length of source string</param>
		/// <param name="lpDstString">Normalised destination string</param>
		/// <param name="cwDstLength">length of destination string</param>
		/// <returns>length of result string</returns>
		[DllImport("Normaliz.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
		private static extern int NormalizeString(Globals.NORM_FORM NormForm, string lpSrcString, int cwSrcLength, StringBuilder lpDstString, int cwDstLength);
	}
}
