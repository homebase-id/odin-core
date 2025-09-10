namespace Odin.Services.Security.PasswordRecovery.Shamir;

public enum ShamirRecoveryState
{
    None,
    
    /// <summary>
    /// Email was sent to owner; we are waiting for the owner to click the email link to verify and enter recovery mode
    /// </summary>
    AwaitingOwnerEmailVerificationToEnterRecoveryMode,

    /// <summary>
    /// Email was sent to owner; we are waiting for the owner to click the email link to verify and exit recovery mode
    /// </summary>
    AwaitingOwnerEmailVerificationToExitRecoveryMode,

    /// <summary>
    /// Players have been notified this owner needs their shards.  We are waiting
    /// for enough players to send their shard to this owner
    /// </summary>
    AwaitingSufficientDelegateConfirmation,
    
    /// <summary>
    /// Owner has all things needed to recover their password.  They just need click the email link and reset their password
    /// </summary>
    AwaitingOwnerFinalization
}