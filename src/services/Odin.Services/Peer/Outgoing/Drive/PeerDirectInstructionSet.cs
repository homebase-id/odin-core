﻿using System;
using System.Collections.Generic;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Services.Peer.Outgoing.Drive
{
    /// <summary>
    /// Specifies what to do with a file when it is uploaded to be sent directly to the recipients
    /// </summary>
    public class PeerDirectInstructionSet
    {
        /// <summary>
        /// The transfer initialization vector used to encrypt the KeyHeader
        /// </summary>
        public byte[] TransferIv { get; set; }

        /// <summary>
        /// List of identities that should receive this file 
        /// </summary>
        public List<string> Recipients { get; set; }

        /// <summary>
        /// The target drive on the recipient's identity; required
        /// </summary>
        public TargetDrive RemoteTargetDrive { get; set; }

        /// <summary>
        /// Optionally specified if you are overwriting a remote file
        /// </summary>
        public Guid? OverwriteGlobalTransitFileId { get; set; }

        public UploadManifest Manifest { get; set; }

        public StorageIntent StorageIntent { get; set; }
    }
}