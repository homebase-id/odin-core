namespace Odin.Services.Membership.Circles
{
    /// <summary>
    /// When a circle's owning app wants members enrolled.  The app declares; the owner disposes, via a
    /// per-app toggle in the owner console.  The effective set is declared AND enabled.
    /// </summary>
    /// <remarks>
    /// Values match the <c>Circle.GrantOn</c> column, which is indexed so the auto-connect pipeline can
    /// query it directly. Ships dormant: every existing circle is <see cref="None"/>, and nothing reads
    /// this until the enrollment phase.
    /// </remarks>
    public enum CircleGrantOn
    {
        /// <summary>
        /// Manual membership only.  The default, and what every circle that predates this is.
        /// </summary>
        None = 0,

        /// <summary>
        /// Granted at any connection establishment, ambient introductions included.
        /// </summary>
        /// <remarks>
        /// Bound by the deposit-only invariant: write/react drive permissions only, no read beyond
        /// anonymous drives, and no permission keys.  Enforced when the definition is written, because
        /// the review is where the key ceremony lives.
        /// </remarks>
        Connect = 1,

        /// <summary>
        /// Granted only when the connection is created through the owning app's own consent flow, never
        /// ambiently -- the vendor case.  Same deposit-only bound as <see cref="Connect"/>.
        /// </summary>
        OwnFlowConnect = 2,

        /// <summary>
        /// Granted when the owner completes the connection review.  Unlike the ambient values, these may
        /// carry read grants and permission keys: the review is the moment those can be minted.
        /// </summary>
        Review = 3
    }

    /// <summary>
    /// What kind of relationship a circle represents.  Presentation and filtering only -- it never
    /// participates in ACL evaluation, which was considered and rejected as ambient authority resting on
    /// a distributed judgment.
    /// </summary>
    /// <remarks>
    /// An enum rather than a boolean because history says new kinds appear: <see cref="Vendor"/> was
    /// discovered during review of the client proposal, before the schema had even shipped.
    /// </remarks>
    public enum CircleDesignation
    {
        /// <summary>
        /// Intimacy plus visibility: Friends, Family, Emergency Location Access.  The default, and what
        /// user-created circles are.  Contact states derive from these.
        /// </summary>
        Personal = 1,

        /// <summary>
        /// Pure capability, no intimacy claim -- Subscribers is a circle whose grant is read access to
        /// the feed drive.  Membership means customer, not confidant.
        /// </summary>
        Audience = 2,

        /// <summary>
        /// Vendor and institution relationships: the hotel writing purchase history, the bank uploading
        /// statements.  Write-only in practice.
        /// </summary>
        Vendor = 3
    }
}
