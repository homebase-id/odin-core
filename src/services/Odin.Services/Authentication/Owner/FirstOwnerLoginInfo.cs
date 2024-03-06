using System;
using Odin.Core.Time;

namespace Odin.Services.Authentication.Owner;

public class FirstOwnerLoginInfo
{
    public static readonly Guid Key = Guid.Parse("799ff759-4727-4bc7-bf72-e260416fa51a");
    public UnixTimeUtc FirstLoginDate { get; set; }
}