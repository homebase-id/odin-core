﻿using System;

namespace Odin.Core.Services.Transit.SendingHost
{
    public class TransferAttempt
    {
        public Int64 Timestamp { get; set; }
        public TransferFailureReason TransferFailureReason { get; set; }
    }
}