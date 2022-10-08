using System.Collections.Generic;

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
        public bool UseCrossReference { get; set; }
        
        /// <summary>
        /// List of identities that should receive this file 
        /// </summary>
        public List<string> Recipients { get; set; }
    }
}