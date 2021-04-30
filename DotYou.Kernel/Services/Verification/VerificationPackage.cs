using System;

namespace DotYou.Kernel.Services.Verification
{
    /// <summary>
    /// Contains the information required to perform a Sender Message Verification check.
    ///
    /// Note this should stay in Identity.Web instead of Identity.DataClient as this should only be a server to server call
    /// </summary>
    public class VerificationPackage
    {
        public Guid Token { get; set; }
        public string Checksum { get; set; }
    }
}