using System.Collections.Generic;

namespace Odin.Services.Optimization.Cdn;

public class SectionOutput
{
    public string Name { get; set; }

    public List<StaticFile> Files { get; set; }
}