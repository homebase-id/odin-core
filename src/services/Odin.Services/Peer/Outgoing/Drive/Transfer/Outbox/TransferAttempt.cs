using Odin.Core.Time;
using System;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class TransferAttempt
    {
        public UnixTimeUtc Timestamp { get; set; }
        public TransferResult TransferResult { get; set; }
    }
}