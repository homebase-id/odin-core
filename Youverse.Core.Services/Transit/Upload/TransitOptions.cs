using System.Collections.Generic;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit.Upload
{
    /// <summary>
    /// Specifies what to do with a file when it is uploaded
    /// </summary>
    public class TransitOptions
    {
        public List<string> Recipients { get; set; }
    }
}