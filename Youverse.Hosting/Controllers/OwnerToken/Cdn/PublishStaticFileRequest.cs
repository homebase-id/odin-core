using System.Collections.Generic;
using Youverse.Core.Services.Cdn;

namespace Youverse.Hosting.Controllers.OwnerToken.Cdn;

public class PublishStaticFileRequest
{
    public string Filename { get; set; }
        
    public IEnumerable<QueryParamSection> Sections { get; set; }
}