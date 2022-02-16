using System;

namespace Youverse.Hosting.Tests
{
    public class TransitTestUtilsOptions
    {
        public static TransitTestUtilsOptions Default = new TransitTestUtilsOptions()
        {
            ProcessOutbox = false,
            ProcessTransitBox = false
        };
        
        /// <summary>
        /// Indicates the file upload and/or transfer should be done in using the owner endpoints instead of the normal app endpoints.
        /// </summary>
        public bool UseOwnerContext { get; set; }

        /// <summary>
        /// Indicates if the process outbox endpoint should be called after sending a transfer
        /// </summary>
        public bool ProcessOutbox { get; set; }

        /// <summary>
        /// Indicates if the process incoming transfers should be called after sending a transfer
        /// </summary>
        public bool ProcessTransitBox { get; set; }

        /// <summary>
        /// The category id to use on the uploaded app data
        /// </summary>
        public Guid AppDataCategoryId { get; set; }

        // <summary>
        /// The Json content to use on the uploaded app data
        /// </summary>
        public string AppDataJsonContent { get; set; }

        public string PayloadData { get; set; }
    }
}