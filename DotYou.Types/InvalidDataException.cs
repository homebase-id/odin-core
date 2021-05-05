using System;
using System.Runtime.Serialization;

namespace DotYou.Types
{
    /// <summary>
    /// Specifies the data for a given operation is insufficient or invalid.
    /// </summary>
    [Serializable]
    internal class InvalidDataException : Exception
    {
        public InvalidDataException()
        {
        }

        public InvalidDataException(string message) : base(message)
        {
        }

        public InvalidDataException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected InvalidDataException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}