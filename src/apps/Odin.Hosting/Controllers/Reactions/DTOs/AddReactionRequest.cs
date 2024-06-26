using System;
using System.ComponentModel.DataAnnotations;
using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Reactions.DTOs;

public class AddReactionRequest2
{
    /// <summary>
    /// Author of the file being reacted to
    /// </summary>
    [Required]
    public OdinId AuthorOdinId { get; set; }

    /// <summary>
    /// Reaction being added to the file
    /// </summary>
    [Required]
    public string Reaction { get; set; }

    /// <summary>
    /// Drive where the file is located
    /// </summary>
    [Required]
    public TargetDrive TargetDrive { get; set; }

    /// <summary>
    /// FileId of the file being reacted to
    /// </summary>
    [Required]
    public Guid FileId { get; set; }

    /// <summary>
    /// GlobalTransitId of the file being reacted to
    /// </summary>
    [Required]
    public Guid GlobalTransitId { get; set; }
}
