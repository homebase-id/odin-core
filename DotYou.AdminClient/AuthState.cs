using System;
using System.Text;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using DotYou.Types;
using DotYou.Types.ApiClient;
using Microsoft.JSInterop;
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

        public async Task<bool> Login(string password)
        {
            await _localStorage.RemoveItemAsync(AUTH_RESULT_STORAGE_KEY);

            var serverNonce = (await _client.GenerateNonce()).Content;

            var passwordBytes = Convert.FromBase64String(password);
            var saltPasswordBytes = Convert.FromBase64String(serverNonce.SaltPassword64);
            var saltKekBytes = Convert.FromBase64String(serverNonce.SaltKek64);
            var saltNonceBytes = Convert.FromBase64String(serverNonce.Nonce64);

            var hashedPassword64 = await _js.InvokeAsync<string>("wrapPbkdf2HmacSha256", passwordBytes, saltPasswordBytes, 100000, 16);
            var hashedPasswordBytes = Convert.FromBase64String(hashedPassword64);
            var hashedNonce64 = await _js.InvokeAsync<string>("wrapPbkdf2HmacSha256", hashedPasswordBytes, saltNonceBytes, 100000, 16);
          
            var hashedKek64 = await _js.InvokeAsync<string>("wrapPbkdf2HmacSha256", passwordBytes, saltKekBytes, 100000, 16);
            
            NonceReplyPackage clientReply = new NonceReplyPackage()
            {
                Nonce64 = serverNonce.Nonce64,
                KeK64 = hashedKek64,
                NonceHashedPassword = hashedNonce64
            };

            var response = await _client.Authenticate(clientReply);

            if (response.IsSuccessStatusCode)
            {
                _authResult = response.Content;
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