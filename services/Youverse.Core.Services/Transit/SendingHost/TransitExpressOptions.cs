using System;
using System.Collections.Generic;
using Youverse.Core.Services.Drives;

namespace Youverse.Core.Services.Transit.SendingHost
{
    /// <summary>
    /// Specifies what to do with a file when it is uploaded
    /// </summary>
    public class TransitExpressOptions
    {
        /// <summary>
        /// List of identities that should receive this file 
        /// </summary>
        public List<string> Recipients { get; set; }

        /// <summary>
        /// Options for when to send the file(s)
        /// </summary>
        public ScheduleOptions Schedule { get; set; } = ScheduleOptions.SendLater;

        /// <summary>
        /// Optionally specified if you are overwriting a remote file
        /// </summary>
        public GlobalTransitIdFileIdentifier OverwriteGlobalTransitFileId { get; set; }
        
    }
}