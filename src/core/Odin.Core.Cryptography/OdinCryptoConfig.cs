namespace Odin.Core.Cryptography;

public record OdinCryptoConfig(int SaltSize = 16, int HashSize = 16, int Iterations = 100000, int NonceSize = 16);
