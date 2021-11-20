using System;

namespace Youverse.Core.Services.Transit.Encryption
{
    public class EncryptionException : Exception
    {
        public EncryptionException()
        {
        }

        public EncryptionException(string message) : base(message)
        {
        }

        public EncryptionException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}