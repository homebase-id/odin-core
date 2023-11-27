using System;
using System.Runtime.Serialization;

namespace Odin.Core.Services.Peer.ReceivingHost.Quarantine
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
    }
}