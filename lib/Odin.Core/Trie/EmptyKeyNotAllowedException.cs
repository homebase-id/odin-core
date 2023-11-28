using System;

namespace Odin.Core.Trie
{
    public class EmptyKeyNotAllowedException : Exception
    {
        public EmptyKeyNotAllowedException(string message) : base(message)
        {
        }

        public EmptyKeyNotAllowedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}