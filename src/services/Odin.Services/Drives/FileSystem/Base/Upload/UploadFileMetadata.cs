﻿using System;
using Odin.Services.Authorization.Acl;

namespace Odin.Services.Drives.FileSystem.Base.Upload
{
    public class UploadFileMetadata
    {
        public UploadFileMetadata()
        {
            this.AppData = new();
            this.AccessControlList = new AccessControlList() { RequiredSecurityGroup = SecurityGroupType.Owner };
        }

        public Guid? VersionTag { get; set; }

        /// <summary>
        /// If true, the payload is encrypted by the client.  In reality, you SHOULD to encrypt all
        /// data yet there are use cases where we need anonymous users to read data (i.e. some profile attributes, etc.)
        /// </summary>
        public bool IsEncrypted { get; set; }

        public AccessControlList AccessControlList { get; set; }

        public UploadAppFileMetaData AppData { get; set; }

        /// <summary>
        /// When true, this file can be distributed to those with a Data Subscription
        /// </summary>
        public virtual bool AllowDistribution { get; set; }

        /// <summary>
        /// The global transit id to which this file refers
        /// </summary>
        // public virtual ExternalFileIdentifier ReferencedFile { get; set; }
        public virtual GlobalTransitIdFileIdentifier ReferencedFile { get; set; }

        
    }
}