using System;
using System.Runtime.Serialization;

namespace DotYou.TenantHost
{
    [Serializable]
    public class EmptyKeyNotAllowed : Exception
    {
        public EmptyKeyNotAllowed()
        {
        }

        public EmptyKeyNotAllowed(string message) : base(message)
        {
        }

        public EmptyKeyNotAllowed(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected EmptyKeyNotAllowed(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}