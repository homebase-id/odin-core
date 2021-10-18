﻿namespace Youverse.Core.Cryptography
{
    public static class CryptographyConstants
    {
        public const int SALT_SIZE = 16; // size in bytes
        public const int HASH_SIZE = 16; // size in bytes
        public const int ITERATIONS = 100000; // number of pbkdf2 iterations
        public const int NONCE_SIZE = 16; // size in bytes
    }
}