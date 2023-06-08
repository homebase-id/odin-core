using System;
using System.Runtime.Serialization;

namespace Youverse.Core.Services.Transit.ReceivingHost.Quarantine
{
    public class HostToHostTransferException : Exception
    {
        public HostToHostTransferException()
        {
        }

        public HostToHostTransferException(string message) : base(message)
        {
        }

        public HostToHostTransferException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected HostToHostTransferException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}