using System;
using System.Collections.Generic;

namespace Odin.Core.Services.Optimization.Cdn;

/// <summary>
/// Options for how a <see cref="SectionOutput"/> should be created
/// </summary>
public class SectionResultOptions
{
    /// <summary>
    /// If true, the metadata.JsonContent field will be included in each file
    /// </summary>
    public bool IncludeHeaderContent { get; set; }
    
    /// <summary>
    /// If included, the payloads of the given keys will be included
    /// </summary>
    public List<string> PayloadKeys { get; set; }

    /// <summary>
    /// If true, the preview thumbnail will not be included in the f    ile
    /// </summary>
    public bool ExcludePreviewThumbnail { get; set; }
}