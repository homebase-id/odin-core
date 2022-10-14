using System;
using System.Collections.Generic;
using Youverse.Core.Identity;

namespace Youverse.Hosting.Tests
{
    /// <summary>
    /// Data returned when using the TransitTestUtils
    /// </summary>
    public class TransitTestUtilsContext : UploadTestUtilsContext
    {
        public Dictionary<DotYouIdentity, TestSampleAppContext> RecipientContexts { get; set; }

        public Guid? GlobalTransitId { get; set; }
    }
}