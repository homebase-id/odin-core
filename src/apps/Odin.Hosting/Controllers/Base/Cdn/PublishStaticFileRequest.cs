using System.Collections.Generic;
using Odin.Services.Optimization.Cdn;

namespace Odin.Hosting.Controllers.Base.Cdn;

public class PublishStaticFileRequest
{
    public string Filename { get; set; }

    public StaticFileConfiguration Config { get; set; }
    
    public List<QueryParamSection> Sections { get; set; }
}