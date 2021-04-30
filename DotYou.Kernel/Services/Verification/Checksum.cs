using System;
namespace DotYou.Kernel.Services.Verification
{
    public static class Checksum
    {
        public static string ComputeSHA512(string data)
        {
            {
                using (var hasher = System.Security.Cryptography.HashAlgorithm.Create("SHA512"))
                {
                    var bytes = System.Text.Encoding.Unicode.GetBytes(data);
                    var hash = hasher.ComputeHash(bytes);
                    return BitConverter.ToString(hash).Replace("-", "");
                }
            }
        }
    }
}