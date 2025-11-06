namespace Odin.Core.Storage.PubSub;

#nullable enable

public class ChannelPrefix(string prefix)
{
    public string Prefix { get; } = prefix;

    public static implicit operator string(ChannelPrefix channelPrefix)
    {
        return channelPrefix.Prefix;
    }
}
