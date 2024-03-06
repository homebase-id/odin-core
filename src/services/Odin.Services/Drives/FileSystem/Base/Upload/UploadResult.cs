using System;
using System.Collections.Generic;
using Odin.Services.Peer;

namespace Odin.Services.Drives.FileSystem.Base.Upload
{
    /// <summary>
    ///  Specifies how the transfer was handled for each recipient
    /// </summary>
    public class UploadResult
    {
        public UploadResult()
        {
            this.RecipientStatus = new();
        }

        public ExternalFileIdentifier File { get; set; }

        /// <summary>
        /// The cross reference Id specified by the server if TransitOptions.UseCrossReference == true
        /// </summary>
        public Guid? GlobalTransitId { get; set; }

        public GlobalTransitIdFileIdentifier GlobalTransitIdFileIdentifier =>
            GlobalTransitId.HasValue
                ? new GlobalTransitIdFileIdentifier()
                {
                    GlobalTransitId = this.GlobalTransitId.GetValueOrDefault(),
                    TargetDrive = this.File.TargetDrive
                }
                : null;

        public Dictionary<string, TransferStatus> RecipientStatus { get; set; }
        
        /// <summary>
        /// The version tag that resulted as of this upload
        /// </summary>
        public Guid NewVersionTag { get; set; }
        
    }
}