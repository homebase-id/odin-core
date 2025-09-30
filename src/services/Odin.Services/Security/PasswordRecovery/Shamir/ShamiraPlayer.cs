using System.Diagnostics;
using Odin.Core.Identity;

namespace Odin.Services.Security.PasswordRecovery.Shamir;

[DebuggerDisplay("Player: {OdinId} Type: {Type}")]
public class ShamiraPlayer
{
    public OdinId OdinId { get; set; }
    public PlayerType Type { get; set; }
}