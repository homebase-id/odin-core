using System.Collections.Generic;
using Odin.Core.Services.Optimization.Cdn;

namespace Odin.Hosting.Controllers.OwnerToken.Cdn;

public class PublishStaticFileRequest
{
    public string Filename { get; set; }

    public StaticFileConfiguration Config { get; set; }
    
    public List<QueryParamSection> Sections { get; set; }
}