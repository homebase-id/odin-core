using System;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit.Upload
{
    public class UploadFileMetadata
    {
        public UploadFileMetadata()
        {
            this.AppData = new();
            this.AccessControlList = new AccessControlList() {RequiredSecurityGroup = SecurityGroupType.Owner};
        }

        public string ContentType { get; set; }
        
        /// <summary>
        /// If true, the payload is encrypted by the client.  In reality, you SHOULD to encrypt all
        /// data yet there are use cases where we need anonymous users to read data (i.e. some profile attributes, etc.)
        /// </summary>
        public bool PayloadIsEncrypted { get; set; }

        public AccessControlList AccessControlList { get; set; }

        public UploadAppFileMetaData AppData { get; set; }
        
        /// <summary>
        /// When true, this file can be distributed to those with a Data Subscription
        /// </summary>
        public virtual bool AllowDistribution { get; set; }

        /// <summary>
        /// The file about which the feedback is given
        /// </summary>
        public virtual ExternalFileIdentifier ReferencedFile { get; set; }
    }
}