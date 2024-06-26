using System;
using System.ComponentModel.DataAnnotations;
using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Reactions.DTOs;

public class GetReactionsRequest2
{
    /// <summary>
    /// Author of the file being reacted to
    /// </summary>
    [Required]
    public OdinId AuthorOdinId { get; set; }

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

    public int Cursor { get; set; }
    public int MaxRecords { get; set; }
}
