using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Peer;

namespace Odin.Services.Drives.DriveCore.Storage
{
    public enum FileState
    {
        Deleted = 0,
        Active = 1,
        // Archived = 3
    }

    /// <summary>
    /// Metadata about the file being stored.  This data is managed by the system. See AppFileMetaData for
    /// data owned by the app
    /// </summary>
    public class FileMetadata
    {
        public FileMetadata()
        {
            this.File = new InternalDriveFileId()
            {
                DriveId = Guid.Empty,
                FileId = Guid.Empty
            };

            this.AppData = new AppFileMetaData();
            this.Payloads = new List<PayloadDescriptor>();
        }

        public FileMetadata(InternalDriveFileId file)
        {
            this.File = file;
            this.AppData = new AppFileMetaData();
        }

        /// <summary>
        /// A file to which this file references.  I.e. this file is a comment about another file
        /// </summary>
        public GlobalTransitIdFileIdentifier ReferencedFile { get; set; }

        public InternalDriveFileId File { get; set; }

        /// <summary>
        /// A globally unique Id to cross reference this file across Identities 
        /// </summary>
        public Guid? GlobalTransitId { get; set; }

        public FileState FileState { get; set; }

        public Int64 Created { get; set; }

        public Int64 Updated { get; set; }

        public Int64 TransitCreated { get; set; }

        public Int64 TransitUpdated { get; set; }

        public ReactionSummary ReactionPreview { get; set; }

        /// <summary>
        /// If true, the payload is encrypted by the client.  In reality, you SHOULD to encrypt all
        /// data yet there are use cases where we need anonymous users to read data (i.e. some profile attributes, etc.)
        /// </summary>
        public bool IsEncrypted { get; set; }

        /// <summary>
        /// The OdinId of the DI that sent this file.  If null, the file was uploaded by the owner.
        /// </summary>
        public string SenderOdinId { get; set; }

        public AppFileMetaData AppData { get; set; }

        public List<PayloadDescriptor> Payloads { get; set; }

        public Guid? VersionTag { get; set; }

        public PayloadDescriptor GetPayloadDescriptor(string key)
        {
            return Payloads?.SingleOrDefault(pk => pk.Key == key);
        }
    }

    /// <summary>
    /// All of the history of this file being sent to various peer identities
    /// </summary>
    public class RecipientTransferHistory
    {
        public Dictionary<string, RecipientTransferHistoryItem> Items { get; set; } = 
            new (StringComparer.InvariantCultureIgnoreCase);
    }

    public class RecipientTransferHistoryItem
    {
        public UnixTimeUtc LastUpdated { get; set; }

        /// <summary>
        /// Indicates the latest known status of a transfer as of the LastUpdated timestmp.  If null
        /// </summary>
        public LatestProblemStatus? LatestProblemStatus { get; set; }

        /// <summary>
        /// If set, indicates the last version tag of this file that was sent to this recipient
        /// </summary>
        public Guid? LatestSuccessfullyDeliveredVersionTag { get; set; }
    }

    public enum LatestProblemStatus
    {
        /// <summary>
        /// Indicates something failed on the server and the client should retry sending 
        /// </summary>
        ClientMustRetry = 1,

        /// <summary>
        /// The server is attempting recover from the previous send
        /// </summary>
        ServerPendingRetry = 12,

        /// <summary>
        /// Caller does not have access to the recipient server
        /// </summary>
        AccessDenied = 403,

        /// <summary>
        /// The local file cannot be sent due to it's settings or recipient's permissions
        /// </summary>
        LocalFileDistributionDenied = 505
    }
}