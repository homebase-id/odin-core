using System;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using DotYou.Types;
using DotYou.Types.ApiClient;
using Microsoft.JSInterop;
using DotYou.Types.Cryptography;
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
        private readonly IJSRuntime _js;

        public AuthState(IOwnerAuthenticationClient client, ILocalStorageService localStorage, IJSRuntime js)
        {
            _client = client;
            _localStorage = localStorage;
            _js = js;
        }

        public bool IsAuthenticated
        {
            get => _isAuthenticated;
        }

        public AuthenticationResult AuthResult
        {
            get => _authResult;
        }

        public static async Task<IPasswordReply> PreparePassword<T>(string password, ClientNoncePackage serverNonce, IJSRuntime _js) where T :IPasswordReply, new()
        {
            var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
            var saltPasswordBytes = Convert.FromBase64String(serverNonce.SaltPassword64);
            var saltKekBytes = Convert.FromBase64String(serverNonce.SaltKek64);
            var saltNonceBytes = Convert.FromBase64String(serverNonce.Nonce64);

            var hashedPassword64 = await _js.InvokeAsync<string>("wrapPbkdf2HmacSha256", passwordBytes, saltPasswordBytes, 100000, 16);
            var hashedPasswordBytes = Convert.FromBase64String(hashedPassword64);
            var hashNoncePassword64 = await _js.InvokeAsync<string>("wrapPbkdf2HmacSha256", hashedPasswordBytes, saltNonceBytes, 100000, 16);

            var hashedKek64 = await _js.InvokeAsync<string>("wrapPbkdf2HmacSha256", passwordBytes, saltKekBytes, 100000, 16);
            
            Console.WriteLine($"hashedPassword64: {hashedPassword64}");
            Console.WriteLine($"hashedKek64: {hashedKek64}");
            Console.WriteLine($"hashNoncePassword64: {hashNoncePassword64}");
            Console.WriteLine($"Nonce64: {serverNonce.Nonce64}");

            T clientReply = new()
            {
                Nonce64 = serverNonce.Nonce64,
                KeK64 = hashedKek64,
                HashedPassword64 = hashedPassword64,
                NonceHashedPassword64 = hashNoncePassword64
            };

            return clientReply;
        }

        public async Task<bool> Login(string password)
        {
            await _localStorage.RemoveItemAsync(AUTH_RESULT_STORAGE_KEY);

            var nonceResponse = await _client.GenerateNonce();
            var noncePackage = nonceResponse.Content;

            var clientReply = await PreparePassword<AuthenticationNonceReply>(password, noncePackage,_js);

            var authResponse = await _client.Authenticate(clientReply);

            if (authResponse.IsSuccessStatusCode)
            {
                _authResult = authResponse.Content;
                await this.InitializeContext();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets an <see cref="AuthenticationResult"/> from local storage.  If it exists, it will
        /// validate with the DI host
        /// </summary>
        /// <returns></returns>
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
                if (_authResult == null)
                {
                    //load from storage
                    _authResult = await GetValidatedResultFromStorage();
                }
                else
                {
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