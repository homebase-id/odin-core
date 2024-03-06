﻿using System;
using System.Collections.Generic;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Services.Peer.Outgoing.Drive
{
    /// <summary>
    /// Specifies what to do with a file when it is uploaded
    /// </summary>
    public class TransitInstructionSet
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
        /// The target drive on the recipient's identity
        /// </summary>
        public TargetDrive RemoteTargetDrive { get; set; }
        
        /// <summary>
        /// Options for when to send the file(s)
        /// </summary>
        public ScheduleOptions Schedule { get; set; } = ScheduleOptions.SendLater;

        /// <summary>
        /// Optionally specified if you are overwriting a remote file
        /// </summary>
        public Guid? GlobalTransitFileId { get; set; }

        public UploadManifest Manifest { get; set; }
    }
}