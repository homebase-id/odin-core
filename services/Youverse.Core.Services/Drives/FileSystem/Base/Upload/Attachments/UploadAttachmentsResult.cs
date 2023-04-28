using System;
using System.Collections.Generic;
using Youverse.Core.Services.Transit;

namespace Youverse.Core.Services.Drives.FileSystem.Base.Upload.Attachments
{
    /// <summary>
    ///  Specifies how the transfer was handled for each recipient
    /// </summary>
    public class UploadAttachmentsResult
    {
        public UploadAttachmentsResult()
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