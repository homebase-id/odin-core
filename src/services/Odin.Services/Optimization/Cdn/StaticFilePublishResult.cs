using System.Collections.Generic;

namespace Odin.Services.Optimization.Cdn;

public class StaticFilePublishResult
{
    public string Filename { get; set; }
    public List<SectionPublishResult> SectionResults { get; set; }
}