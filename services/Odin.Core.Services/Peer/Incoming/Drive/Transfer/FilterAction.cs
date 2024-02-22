namespace Odin.Core.Services.Peer.Incoming.Drive.Transfer
{
    /// <summary>
    /// The action recommended by the filter
    /// </summary>
    public enum FilterAction
    {
        None = 0,
        Quarantine = 1,
        Reject = 2,
        Accept = 5
    }
}