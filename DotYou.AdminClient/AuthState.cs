using System;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using DotYou.Types;
using DotYou.Types.ApiClient;
using DotYou.Types.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Newtonsoft.Json;

namespace DotYou.AdminClient
{
    public class AuthState
    {
        private const string AUTH_RESULT_STORAGE_KEY = "ARSK";
        private AuthenticationResult _authResult;
        private bool _isAuthenticated;
        private IOwnerAuthenticationClient _client;
        private ILocalStorageService _localStorage;

        public AuthState(IOwnerAuthenticationClient client, ILocalStorageService localStorage)
        {
            _client = client;
            _localStorage = localStorage;
        }

        public bool IsAuthenticated
        {
            get => _isAuthenticated;
        }

        public AuthenticationResult AuthResult
        {
            get => _authResult;
        }

        public async Task<bool> Login(string password)
        {
            await _localStorage.RemoveItemAsync(AUTH_RESULT_STORAGE_KEY);

            var nonceResponse = await _client.GenerateNonce();
            var noncePackage = nonceResponse.Content;

            var hashPassword = KeyDerivation.Pbkdf2(password, Convert.FromBase64String(noncePackage.SaltPassword64), KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);
            var noncePassword = KeyDerivation.Pbkdf2(Convert.ToBase64String(hashPassword), Convert.FromBase64String(noncePackage.Nonce64), KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);
            var keyEncryptionKey = KeyDerivation.Pbkdf2(password, Convert.FromBase64String(noncePackage.SaltKek64), KeyDerivationPrf.HMACSHA512, CryptographyConstants.ITERATIONS, CryptographyConstants.HASH_SIZE);
            
            AuthenticationNonceReply clientReply = new AuthenticationNonceReply()
            {
                Nonce64 = noncePackage.Nonce64,
                KeK64 = Convert.ToBase64String(keyEncryptionKey),
                NonceHashedPassword64 = Convert.ToBase64String(noncePassword)
            };

            var authResponse = await _client.Authenticate(clientReply);

            if (authResponse.IsSuccessStatusCode)
            {
                _authResult = authResponse.Content;
                await this.InitializeContext();
                return true;
            }

            return false;
        }

        private async Task<AuthenticationResult> GetValidatedResultFromStorage()
        {
            var exists = await _localStorage.ContainKeyAsync(AUTH_RESULT_STORAGE_KEY);
            if (exists)
            {
                string json = await _localStorage.GetItemAsync<string>(AUTH_RESULT_STORAGE_KEY);
                var cachedResult = JsonConvert.DeserializeObject<AuthenticationResult>(json);

                if (cachedResult == null)
                {
                    return null;
                }

                var response = await _client.IsValid(cachedResult.Token);
                await response.EnsureSuccessStatusCodeAsync();

                var isValid = response.Content;
                if (isValid)
                {
                    return cachedResult;
                }
            }

            return null;
        }

        public async Task InitializeContext()
        {
            try
            {
                var r = _authResult ?? await GetValidatedResultFromStorage();
                if (r != null)
                {
                    _authResult = r;
                    _isAuthenticated = true;
                    await _localStorage.SetItemAsync(AUTH_RESULT_STORAGE_KEY, JsonConvert.SerializeObject(_authResult));
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to initialize context", ex);
            }
        }
    }
}