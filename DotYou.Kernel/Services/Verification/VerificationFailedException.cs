using System;
using System.Runtime.Serialization;

namespace DotYou.Kernel.Services.Verification
{
    [Serializable]
    internal class VerificationFailedException : Exception
    {
        public VerificationFailedException()
        {
        }

        public VerificationFailedException(string message) : base(message)
        {
        }

        public VerificationFailedException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected VerificationFailedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}