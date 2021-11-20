using System.Collections.Generic;
using Newtonsoft.Json;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Transit.Upload
{
    [JsonConverter(typeof(RecipientListJsonConverter))]
    public class RecipientList
    {
        public List<DotYouIdentity> Recipients { get; set; }
    }

    //TODO: uhg - Not sure why I had to build this for such a simple list.  maybe revisit one day
}