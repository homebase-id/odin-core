using System;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using DotYou.Types;

namespace DotYou.AdminClient
{
    public class AuthState
    {
        private const string TOKEN_STORAGE_KEY = "TK";
        private Guid _token;
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

        public Guid Token
        {
            get => _token;
        }

        public async Task<bool> Login(string password)
        {
            await _localStorage.RemoveItemAsync(TOKEN_STORAGE_KEY);
            var response = await _client.Authenticate(password);

            if (response.IsSuccessStatusCode)
            {
                _token = response.Content;
                await this.InitializeContext();
                return true;
            }

            return false;
        }

        public async Task InitializeContext()
        {
            try
            {
                var exists = await _localStorage.ContainKeyAsync(TOKEN_STORAGE_KEY);

                if (exists)
                {
                    _token = await _localStorage.GetItemAsync<Guid>(TOKEN_STORAGE_KEY);
                }

                var response = await _client.IsValid(_token);

                await response.EnsureSuccessStatusCodeAsync();

                _isAuthenticated = response.Content;

                if (_isAuthenticated)
                {
                    await _localStorage.SetItemAsync(TOKEN_STORAGE_KEY, _token);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to initialize context", ex);
            }
        }
    }
}