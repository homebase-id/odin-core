using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Services.Drives.DriveCore.Storage;

namespace Odin.Hosting.Tests
{
    /// <summary>
    /// Data returned when using the TransitTestUtils
    /// </summary>
    public class TransitTestUtilsContext : UploadTestUtilsContext
    {
        public Dictionary<OdinId, TestAppContext> RecipientContexts { get; set; }

        public Guid? GlobalTransitId { get; set; }

        public List<ImageDataHeader> Thumbnails { get; set; }
    }
}