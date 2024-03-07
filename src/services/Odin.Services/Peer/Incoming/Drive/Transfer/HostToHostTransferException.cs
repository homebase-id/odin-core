using System;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
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