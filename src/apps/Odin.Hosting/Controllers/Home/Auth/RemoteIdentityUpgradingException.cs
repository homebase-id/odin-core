using System;

namespace Odin.Hosting.Controllers.Home.Auth
{
    /// <summary>
    /// The identity being signed in with is running its version upgrade and refused the call.
    /// </summary>
    /// <remarks>
    /// Its own type so the YouAuth callback can tell it apart from a genuine failure without
    /// inspecting strings.  The distinction is worth a type: every other failure means "this did not
    /// work", while this one means "this will work shortly" -- and an upgrade starts precisely when
    /// the owner logs in to approve the sign-in, so it lands in the middle of the flow rather than
    /// rarely.
    /// </remarks>
    public sealed class RemoteIdentityUpgradingException : Exception
    {
        public RemoteIdentityUpgradingException()
            : base("The remote identity is running a version upgrade")
        {
        }
    }
}
