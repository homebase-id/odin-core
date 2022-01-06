using System;
using System.Runtime.Serialization;

namespace Youverse.Core.Services.Transit.Upload
{
    public class UploadException : Exception
    {
        public UploadException()
        {
        }

        public UploadException(string message) : base(message)
        {
        }

        public UploadException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected UploadException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}