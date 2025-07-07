using System.Collections.Generic;

namespace Odin.Services.LinkPreview.Profile;

public class AboutSection
{
    public List<string> Status { get; init; } = new();
    public List<string> Bio { get; init; } = new();
    public List<ExperienceAttribute> Experience { get; init; } = new();
    public List<string> ShortBio { get; init; } = new();
}