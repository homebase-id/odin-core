namespace Odin.Services.ShamiraPasswordRecovery;

public enum PlayerType
{
    /// <summary>
    /// The dealer can request the copy of the (encrypted) shard from a (machine) player
    /// </summary>
    Automatic = 1,

    /// <summary>
    /// The (human) player must click OK to release a shard to the dealer.
    /// </summary>
    Delegate = 2,

    /// <summary>
    /// The (human) player must give the information out-of-band, Not yet implemented
    /// </summary>
    Manual = 3
}