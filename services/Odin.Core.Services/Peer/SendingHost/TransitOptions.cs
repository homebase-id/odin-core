using System;
using System.Collections.Generic;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Peer.SendingHost
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
        public ScheduleOptions Schedule { get; set; } = ScheduleOptions.SendLater;

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
    }

    [Flags]
    public enum SendContents
    {
        Header = 1,
        Payload = 2,
        All = Header | Payload
    }

    public enum ScheduleOptions
    {
        /// <summary>
        /// Sends file now; blocks the return of the thread until a response is received from the all recipients.
        /// </summary>
        SendNowAwaitResponse = 1,

        /// <summary>
        /// Sends immediately from the same thread as the caller but spawns a new thread so the caller's request
        /// instantly returns.  For each failed recipient, the file is moved to ScheduleOptions.SendLater 
        /// </summary>
        //SendNowFireAndForget
        SendLater = 2
    }
}