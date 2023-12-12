using System;
using System.Security;

namespace Odin.Core.Cryptography.Crypto
{
    public static class BIP39
    {
        public static string GenerateBIP39(byte[] key)
        {
            if (key.Length != 16)
                throw new ArgumentException("Length must be 16 (for now - easy to remove this check)");

            var bip39 = new Bitcoin.BIP39.BIP39(key, "", Bitcoin.BIP39.BIP39.Language.English);

            // Check that we can decode it
            // This shouldn't be necessary, but better safe than sorry (issuing invalid menmonic)
            var bip39FromMnemonic = new Bitcoin.BIP39.BIP39(bip39.MnemonicSentence, "", Bitcoin.BIP39.BIP39.Language.English);
            if (ByteArrayUtil.EquiByteArrayCompare(bip39FromMnemonic.EntropyBytes, key) == false)
                throw new Exception("BIP39 algorithm error");

            return bip39.MnemonicSentence;
        }

        public static SensitiveByteArray DecodeBIP39(string mnemonicStr)
        {
            var bip39FromMnemonic = new Bitcoin.BIP39.BIP39(mnemonicStr, "", Bitcoin.BIP39.BIP39.Language.English);

            return new SensitiveByteArray(bip39FromMnemonic.EntropyBytes);
        }
    }
}