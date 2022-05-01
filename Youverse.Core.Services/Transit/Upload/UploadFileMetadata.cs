﻿using Youverse.Core.Services.Authorization.Acl;

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

        public string SenderDotYouId { get; set; }
        
        /// <summary>
        /// If true, the payload is encrypted by the client.  In reality, you SHOULD to encrypt all
        /// data yet there are use cases where we need anonymous users to read data (i.e. some profile attributes, etc.)
        /// </summary>
        public bool PayloadIsEncrypted { get; set; }

        public AccessControlList AccessControlList { get; set; }

        public UploadAppFileMetaData AppData { get; set; }
    }
}