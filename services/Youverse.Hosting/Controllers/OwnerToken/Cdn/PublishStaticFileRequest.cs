using System.Collections.Generic;
using Youverse.Core.Services.Optimization.Cdn;

namespace Youverse.Hosting.Controllers.OwnerToken.Cdn;

public class PublishStaticFileRequest
{
    public string Filename { get; set; }

    public StaticFileConfiguration Config { get; set; }
    
    public List<QueryParamSection> Sections { get; set; }
}