using Youverse.Core.Services.Authorization.Acl;

namespace Youverse.Core.Services.Drive.Core.Storage
{
    public class ServerMetadata
    {
        public AccessControlList AccessControlList { get; set; }
        
        /// <summary>
        /// If true, the file should not be indexed
        /// </summary>
        public bool DoNotIndex { get; set; }

    }
}