using System.Collections.Generic;
using Youverse.Core.Identity;
using Youverse.Hosting.Tests.AppAPI;

namespace Youverse.Hosting.Tests
{
    /// <summary>
    /// Data returned when using the TransitTestUtils
    /// </summary>
    public class TransitTestUtilsContext : UploadTestUtilsContext
    {
        public Dictionary<DotYouIdentity, TestSampleAppContext> RecipientContexts { get; set; }
    }
}