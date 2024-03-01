using System;
using System.Security;
using Bitcoin.BIP39;

namespace Odin.Core.Cryptography.Crypto
{
    public static class BIP39Util
    {
        public static string GenerateBIP39(byte[] key)
        {
            if (key.Length != 16)
                throw new ArgumentException("Length must be 16 (for now - easy to remove this check)");

            var bip39 = new BIP39(key, "", BIP39.Language.English);

            // Check that we can decode it
            // This shouldn't be necessary, but better safe than sorry (issuing invalid menmonic)
            var bip39FromMnemonic = new BIP39(bip39.MnemonicSentence, "", BIP39.Language.English);
            if (ByteArrayUtil.EquiByteArrayCompare(bip39FromMnemonic.EntropyBytes, key) == false)
                throw new Exception("BIP39 algorithm error");

            return bip39.MnemonicSentence;
        }

        public static SensitiveByteArray DecodeBIP39(string mnemonicStr)
        {
            var bip39FromMnemonic = new BIP39(mnemonicStr, "", BIP39.Language.English);

            return new SensitiveByteArray(bip39FromMnemonic.EntropyBytes);
        }
    }
}