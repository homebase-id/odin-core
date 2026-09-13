using System;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives.FileSystem.Base.Upload
{
    public class UploadFileMetadata
    {
        public Guid? VersionTag { get; set; }

        /// <summary>
        /// If true, the payload is encrypted by the client.  In reality, you SHOULD to encrypt all
        /// data yet there are use cases where we need anonymous users to read data (i.e. some profile attributes, etc.)
        /// </summary>
        public bool IsEncrypted { get; set; }

        public AccessControlList AccessControlList { get; set; } = new() { RequiredSecurityGroup = SecurityGroupType.Owner };

        public UploadAppFileMetaData AppData { get; set; } = new();

        /// <summary>
        /// When true, this file can be distributed to those with a Data Subscription
        /// </summary>
        public bool AllowDistribution { get; set; }

        /// <summary>
        /// The global transit id to which this file refers
        /// </summary>
        public GlobalTransitIdFileIdentifier ReferencedFile { get; set; }

        /// <summary>
        /// Specifies the identity that holds the payload content
        /// </summary>
        public DataSource DataSource { get; set; }

        /// <summary>
        /// When this file should expire, in milliseconds. <c>0</c> never, <c>&gt; 0</c> an absolute
        /// <see cref="Odin.Core.Time.UnixTimeUtc"/>, <c>&lt; 0</c> a duration measured from the first
        /// payload read. Use <see cref="FileTtl.After"/> / <see cref="FileTtl.AfterFirstRead"/> rather
        /// than a raw number. Travels to recipients, which is how group retention works.
        /// </summary>
        public long Ttl { get; set; }
    }
}