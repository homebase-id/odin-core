namespace Odin.Services.Security.PasswordRecovery.Shamir;

public enum ShamirRecoveryState
{
    None,
    
    /// <summary>
    /// Email was sent to owner; we are waiting for the owner to click the email link to verify and enter recovery mode
    /// </summary>
    AwaitingOwnerEmailVerificationToEnterRecoveryMode,

    /// <summary>
    /// No longer set: asking to exit only sends the email, and recovery goes on as it was until the
    /// owner confirms with it (anyone can ask).  Kept so a status stored before that still reads.
    /// </summary>
    AwaitingOwnerEmailVerificationToExitRecoveryMode,

    /// <summary>
    /// Players have been notified this owner needs their shards.  We are waiting
    /// for enough players to send their shard to this owner
    /// </summary>
    AwaitingSufficientDelegateConfirmation,
    
    /// <summary>
    /// No longer set: the server rebuilt a server-made deal's secret and emailed a finalize link.  Every
    /// deal now waits in <see cref="AwaitingRecoveringBrowser"/>.  Kept so a status stored before still reads.
    /// </summary>
    AwaitingOwnerFinalization,

    /// <summary>
    /// Enough shards are in, for any deal.  The browser that started recovery fetches them, sealed
    /// to its throwaway key, and rebuilds the secret itself.
    /// </summary>
    AwaitingRecoveringBrowser
}