namespace Youverse.Core.Services.Authentication
{
    public sealed class SaltsPackage
    {
        public string SaltPassword64 { get; set; }
        public string SaltKek64 { get; set; }
    }
}