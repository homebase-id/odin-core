﻿using System;

namespace Odin.Core.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class TransferAttempt
    {
        public Int64 Timestamp { get; set; }
        public TransferResult TransferResult { get; set; }
    }
}