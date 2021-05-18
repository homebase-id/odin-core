using System;
using System.Runtime.Serialization;

namespace DotYou.Types.Messaging
{
    /// <summary>
    /// Specifies the data for a given operation is insufficient or invalid.
    /// </summary>
    [Serializable]
    public class MessageSendException : Exception
    {
        public MessageSendException()
        {
        }

        public MessageSendException(string message) : base(message)
        {
        }

        public MessageSendException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected MessageSendException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}