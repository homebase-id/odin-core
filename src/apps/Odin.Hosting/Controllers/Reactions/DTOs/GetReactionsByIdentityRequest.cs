using System;
using System.ComponentModel.DataAnnotations;
using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Reactions.DTOs;

public class GetReactionsByIdentityRequest2
{
    [Required]
    public OdinId Identity { get; set; }
    [Required]
    public OdinId AuthorOdinId { get; set; }
    [Required]
    public TargetDrive TargetDrive { get; set; }
    [Required]
    public Guid FileId { get; set; }
    [Required]
    public Guid GlobalTransitId { get; set; }

    public int Cursor { get; set; }
    public int MaxRecords { get; set; }
}