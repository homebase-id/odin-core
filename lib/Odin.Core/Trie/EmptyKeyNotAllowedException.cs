using System;
using System.Runtime.Serialization;

namespace Odin.Core.Trie
{
    [Serializable]
    public class EmptyKeyNotAllowedException : Exception
    {
        public EmptyKeyNotAllowedException()
        {
        }

        public EmptyKeyNotAllowedException(string message) : base(message)
        {
        }

        public EmptyKeyNotAllowedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected EmptyKeyNotAllowedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}