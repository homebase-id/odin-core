using System;
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
        /// If true, the file is hard-deleted when all recipients have received the file.
        /// </summary>
        public bool IsTransient { get; set; }

        /// <summary>
        /// List of identities that should receive this file 
        /// </summary>
        public List<string> Recipients { get; set; }

        /// <summary>
        /// When true, transfer history will not be tracked for this file.
        /// </summary>
        public bool DisableTransferHistory { get; set; }

        public bool UseAppNotification { get; set; }

        public AppNotificationOptions AppNotificationOptions { get; set; }

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
        /// Sets the fileId on which this file depends when sending over peer
        /// </summary>
        public Guid? OutboxDependencyFileId { get; set; }

        public OutboxPriority Priority { get; set; } = OutboxPriority.Low;
    }
    
    [Flags]
    public enum OutboxPriority
    {
        High,
        Medium,
        Low
    }
    
    [Flags]
    public enum SendContents
    {
        Header = 1,
        Payload = 2,
        All = Header | Payload
    }
}