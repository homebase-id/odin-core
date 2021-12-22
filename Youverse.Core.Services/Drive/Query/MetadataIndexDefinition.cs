using System;
using Newtonsoft.Json.Linq;

namespace Youverse.Core.Services.Drive.Query
{
    public class MetadataIndexDefinition
    {
        public Guid CategoryId { get; set; }

        /// <summary>
        /// If true, the <see cref="JsonContent"/> is the full payload of information, otherwise, it is partial (like a preview of a chat message)
        /// </summary>
        public bool ContentIsComplete { get; set; }
        
        /// <summary>
        /// The JsonPayload to be included in the index.  This is not searchable but rather available to be returned
        /// when querying the index so you do not have to retrieve the whole payload
        /// </summary>
        public string JsonContent { get; set; }
        
    }
}