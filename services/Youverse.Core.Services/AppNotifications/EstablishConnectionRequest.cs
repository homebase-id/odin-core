﻿using System.Collections.Generic;
using Youverse.Core.Services.Drives;

namespace Youverse.Core.Services.AppNotifications;

public class EstablishConnectionRequest
{
    /// <summary>
    /// List of drives from which the app wants notifications
    /// </summary>
    public List<TargetDrive> Drives { get; set; }
}

public class SocketCommand
{
    public SocketCommandType Command { get; set; }

    public string Data { get; set; }
}

public enum SocketCommandType
{
    ProcessTransitInstructions = 111,
    
    ProcessInbox = 222
}