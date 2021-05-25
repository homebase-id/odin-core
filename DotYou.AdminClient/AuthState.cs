using System;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using DotYou.Types;
using DotYou.Types.ApiClient;
using Newtonsoft.Json;

namespace DotYou.AdminClient
{
    public class AuthState
    {
        private const string AUTH_RESULT_STORAGE_KEY = "ARSK";
        private AuthenticationResult _authResult;
        private bool _isAuthenticated;
        private IAdminAuthenticationClient _client;
        private ILocalStorageService _localStorage;

        public AuthState(IAdminAuthenticationClient client, ILocalStorageService localStorage)
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
            var response = await _client.Authenticate(password);

            if (response.IsSuccessStatusCode)
            {
                _authResult = response.Content;
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