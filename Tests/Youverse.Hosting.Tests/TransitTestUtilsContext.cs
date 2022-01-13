using System.Collections.Generic;
using Youverse.Core.Identity;

namespace Youverse.Hosting.Tests.AppAPI
{
    /// <summary>
    /// Data returned when using the TransitTestUtils
    /// </summary>
    public class TransitTestUtilsContext : UploadTestUtilsContext
    {
        public Dictionary<DotYouIdentity, TestSampleAppContext> RecipientContexts { get; set; }
    }
}