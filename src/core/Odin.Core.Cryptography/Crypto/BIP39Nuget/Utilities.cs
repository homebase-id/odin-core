using Odin.Core;
using Org.BouncyCastle.Crypto.Digests;
using System;
using System.Collections;
using System.Linq;
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
		/// Converts a hex based string into its bytes contained in a byte array
		/// </summary>
		/// <param name="hex">The hex encoded string</param>
		/// <returns>the bytes derived from the hex encoded string</returns>
		public static byte[] HexStringToBytes(string hexString)
		{
			return Enumerable.Range(0, hexString.Length).Where(x => x % 2 == 0).Select(x => Convert.ToByte(hexString.Substring(x, 2), 16)).ToArray();
		}

		/// <summary>
		/// Turns a byte array into a Hex encoded string
		/// </summary>
		/// <param name="bytes">The bytes to encode to hex</param>
		/// <param name="upperCase"></param>
		/// <returns>The hex encoded representation of the bytes</returns>
		public static string BytesToHexString(byte[] bytes, bool upperCase = false)
		{
			var result = Convert.ToHexString(bytes);
			return upperCase ? result : result.ToLower();
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

        public static string NormaliseStringNfkd(string toNormalise)
        {
            if (toNormalise == null)
            {
                throw new ArgumentNullException(nameof(toNormalise));
            }

            char[] trim = { '\0' };
            return toNormalise.Normalize(NormalizationForm.FormKD).TrimEnd(trim);
        }
	}
}
