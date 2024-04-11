﻿using System;
using System.Collections.Generic;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive
{
    /// <summary>
    /// Specifies what to do with a file when it is uploaded
    /// </summary>
    public class TransitOptions
    {

        /// <summary>
        /// Specifies if a CrossReferenceId should be added to the file when sending to other Identities
        /// </summary>
        public bool UseGlobalTransitId { get; set; }

        /// <summary>
        /// List of identities that should receive this file 
        /// </summary>
        public List<string> Recipients { get; set; }

        public bool UseAppNotification { get; set; }

        public AppNotificationOptions AppNotificationOptions { get; set; }

        /// <summary>
        /// Options for when to send the file(s)
        /// </summary>
        public PriorityOptions Priority { get; set; } = PriorityOptions.Medium;

        /// <summary>
        /// Specifies which parts of the file to send
        /// </summary>
        public SendContents SendContents { get; set; } = SendContents.All;

        /// <summary>
        /// If set, the target drive will be this one instead of that from the file
        /// </summary>
        public TargetDrive RemoteTargetDrive { get; set; }

        /// <summary>
        /// If set, the recipient will receive this in the file's metadata instead of that in the file
        /// </summary>
        //TODO: hack - This is a hack in place for alpha to support transit direct send
        public Guid? OverrideRemoteGlobalTransitId { get; set; }
        
        /// <summary>
        /// If specified, this item will not be sent before the id specified in OutboxDependencyId 
        /// </summary>
        public Guid? OutboxDependencyFileId { get; set; }

    }

    [Flags]
    public enum SendContents
    {
        Header = 1,
        Payload = 2,
        All = Header | Payload
    }

    public enum PriorityOptions
    {
        /// <summary>
        /// Sends file now; blocks the return of the thread until a response is received from the all recipients.
        /// </summary>
        High = 1,
        
        Medium = 2,
        
        Low = 3
    }
}