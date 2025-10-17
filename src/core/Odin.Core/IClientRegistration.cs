using System;
using Odin.Core.Identity;

namespace Odin.Core;

public interface IClientRegistration
{
    public Guid Id { get; set; }
    public OdinId IssuedTo { get; set; }
    public int Type { get; set; }
    public long TimeToLiveSeconds { get; set; }
    
    /// <summary>
    /// Categorizes this registration, giving you a way to get a list by something like AppId
    /// </summary>
    public Guid CategoryId { get; set; }
    string GetValue();
}