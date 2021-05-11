using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using DotYou.Types;
using DotYou.Types.Identity;
using Refit;

namespace DotYou.AdminClient
{
    public class AppState
    {
        private const string TOKEN_STORAGE_KEY = "TK";
        private Guid _token;
        private readonly UserContext _user;
        private HttpClient _http;
        private ILocalStorageService _localStorage;

        public AppState(HttpClient client, ILocalStorageService localStorage)
        {
            _http = client;
            _localStorage = localStorage;
            _user = new UserContext();
        }

        public UserContext User
        {
            get => _user;
        }

        public async Task<bool> Login(string password)
        {
            await _localStorage.RemoveItemAsync(TOKEN_STORAGE_KEY);
            var authService = RestService.For<IAdminAuthenticationClient>(_http);
            var response = await authService.Authenticate(password);

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

                var authService = RestService.For<IAdminAuthenticationClient>(_http);
                var identityService = RestService.For<IAdminIdentityAttributeClient>(_http);

                var response = await authService.IsValid(_token);

                await response.EnsureSuccessStatusCodeAsync();

                _user.IsAuthenticated = response.Content;

                if (_user.IsAuthenticated)
                {
                    var nameResponse = await identityService.GetPrimaryName();
                    await nameResponse.EnsureSuccessStatusCodeAsync();

                    var uriResponse = await identityService.GetPrimaryAvatarUri();
                    await uriResponse.EnsureSuccessStatusCodeAsync();

                    if (nameResponse.Content != null)
                    {
                        var name = nameResponse.Content;
                        _user.Identity = "TODO";
                        _user.Surname = name.Surname;
                        _user.GivenName = name.Personal;
                    }
                    
                    _user.AvatarUri = uriResponse.Content;

                    await _localStorage.SetItemAsync(TOKEN_STORAGE_KEY, _token);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to initialize YFContext", ex);
            }
        }
    }
}