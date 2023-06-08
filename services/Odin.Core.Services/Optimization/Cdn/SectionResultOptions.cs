namespace Odin.Core.Services.Optimization.Cdn;

/// <summary>
/// Options for how a <see cref="SectionOutput"/> should be created
/// </summary>
public class SectionResultOptions
{
    /// <summary>
    /// If true, the content of the additional thumbnails defined in the metadata will be included in each file.
    /// </summary>
    public bool IncludeAdditionalThumbnails { get; set; }

    /// <summary>
    /// If true, the metadata.JsonContent field will be included in each file
    /// </summary>
    public bool IncludeJsonContent { get; set; }

    /// <summary>
    /// If true, the payload of each file will be included.
    /// </summary>
    public bool IncludePayload { get; set; }

    /// <summary>
    /// If true, the preview thumbnail will not be included in the f    ile
    /// </summary>
    public bool ExcludePreviewThumbnail { get; set; }
}