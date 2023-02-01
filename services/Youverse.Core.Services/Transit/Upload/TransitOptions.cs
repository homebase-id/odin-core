using System;
using System.Collections.Generic;
using Youverse.Core.Services.Authorization.ExchangeGrants;

namespace Youverse.Core.Services.Transit.Upload
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

        /// <summary>
        /// Options for when to send the file(s)
        /// </summary>
        public ScheduleOptions Schedule { get; set; } = ScheduleOptions.SendLater;

        /// <summary>
        /// Specifies which parts of the file to send
        /// </summary>
        public SendContents SendContents { get; set; } = SendContents.All;
    }

    
    [Flags]
    public enum SendContents
    {
        Header = 1,
        Thumbnails = 2,
        Payload = 4,
        All = Header | Thumbnails | Payload
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