using System.Collections.Generic;

namespace Odin.Services.LinkPreview.Profile;

public class ProfileBlock
{
    public string Id { get; set; }
    public string Type { get; set; }
    public string SectionId { get; set; }
    public int? Priority { get; set; }
    public Dictionary<string, object> Data { get; set; }
    public TypeDefinition TypeDefinition { get; set; }
    public string ProfileId { get; set; }
}