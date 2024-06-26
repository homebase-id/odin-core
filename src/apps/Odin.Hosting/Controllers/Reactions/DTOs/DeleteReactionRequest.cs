using System;
using System.ComponentModel.DataAnnotations;
using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Reactions.DTOs;

public class DeleteReactionRequest2
{
    /// <summary>
    /// Author of the file the reaction is being deleted from
    /// </summary>
    [Required]
    public OdinId AuthorOdinId { get; set; }

    /// <summary>
    /// Reaction being deleted from the file
    /// </summary>
    [Required]
    public string Reaction { get; set; }

    /// <summary>
    /// Drive where the file is located
    /// </summary>
    [Required]
    public TargetDrive TargetDrive { get; set; }

    /// <summary>
    /// FileId of the file the reaction is being deleted from
    /// </summary>
    [Required]
    public Guid FileId { get; set; }

    /// <summary>
    /// GlobalTransitId of the file the reaction is being deleted from
    /// </summary>
    [Required]
    public Guid GlobalTransitId { get; set; }
}