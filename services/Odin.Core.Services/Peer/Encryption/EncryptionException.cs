using System;

namespace Odin.Core.Services.Peer.Encryption
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