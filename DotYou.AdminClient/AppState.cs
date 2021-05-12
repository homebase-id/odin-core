using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using DotYou.Types;
using DotYou.Types.ApiClient;
using DotYou.Types.Identity;
using Refit;

namespace DotYou.AdminClient
{
    public class AppState
    {
        private readonly UserContext _user;
        private AuthState _authState;
        private readonly IAdminIdentityAttributeClient _client;
        private ILocalStorageService _localStorage;

        public AppState(AuthState authState, IAdminIdentityAttributeClient client, ILocalStorageService localStorage)
        {
            _authState = authState;
            _client = client;
            _localStorage = localStorage;
            _user = new UserContext();
        }

        public UserContext User
        {
            get => _user;
        }

        public async Task<bool> Login(string password)
        {
            var success = await _authState.Login(password);

            if (success)
            {
                await this.InitializeContext();
            }

            return success;
        }

        public async Task InitializeContext()
        {
            try
            {
                if (_authState.IsAuthenticated)
                {
                    
                    var nameResponse = await _client.GetPrimaryName();
                    await nameResponse.EnsureSuccessStatusCodeAsync();
                    
                    var uriResponse = await _client.GetPrimaryAvatarUri();
                    await uriResponse.EnsureSuccessStatusCodeAsync();
                    
                    if (nameResponse.Content != null)
                    {
                        var name = nameResponse.Content;
                        _user.Identity = "TODO";
                        _user.Surname = name.Surname;
                        _user.GivenName = name.Personal;
                    }

                    if (uriResponse.Content != null)
                    {
                        _user.AvatarUri = uriResponse.Content;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to initialize App state", ex);
            }
        }
    }
}