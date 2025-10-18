using System;

namespace Odin.Core;

public interface IClientRegistration
{
    public Guid Id { get; }
    public string IssuedTo { get; }
    public int Type { get; }
    public long TimeToLiveSeconds { get; }

    /// <summary>
    /// Categorizes this registration, giving you a way to get a list by something like AppId
    /// </summary>
    public Guid CategoryId { get; }

    string GetValue();
}