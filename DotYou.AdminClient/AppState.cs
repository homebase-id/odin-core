using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using DotYou.AdminClient.Services;
using DotYou.Types;
using DotYou.Types.ApiClient;
using DotYou.Types.Circle;
using DotYou.Types.Identity;
using DotYou.Types.Messaging;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Refit;

namespace DotYou.AdminClient
{
    public class AppState
    {
        private readonly UserContext _user;
        private AuthState _authState;
        private readonly IAdminIdentityAttributeClient _client;
        private readonly NavigationManager _nav;
        private readonly IClientNotificationEvents _notificationEventHandler;
        private HubConnection _connection;
        
        public AppState(AuthState authState, IAdminIdentityAttributeClient client, NavigationManager nav, IClientNotificationEvents notificationEventHandler)
        {
            _authState = authState;
            _client = client;
            _nav = nav;
            _notificationEventHandler = notificationEventHandler;
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
                        _user.DotYouId = _authState.AuthResult.DotYouId;
                        _user.Surname = name.Surname;
                        _user.GivenName = name.Personal;
                    }

                    if (uriResponse.Content != null)
                    {
                        _user.AvatarUri = uriResponse.Content;
                    }

                    await InitializeNotifications();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to initialize App state", ex);
            }
        }
        
        private async Task InitializeNotifications()
        {
            UriBuilder b = new UriBuilder(_nav.BaseUri);
            b.Path = "live/notifications";

            _connection = new HubConnectionBuilder()
                .WithUrl(b.Uri.AbsoluteUri, options =>
                {
                    //TODO: SkipNegotiation disabled for prototrial so we can use the auth token
                    options.SkipNegotiation = true;    
                    options.Transports = HttpTransportType.WebSockets;
                    options.AccessTokenProvider = ()=>
                    {
                        Guid token = _authState.AuthResult?.Token ?? Guid.Empty;
                        return Task.FromResult(token.ToString());
                    };
                })
                .WithAutomaticReconnect()
                .Build();

            // connection.On<CircleInvite>(nameof(INotificationHub.NotificationOfCircleInvite), (invite) =>
            // {
            //     notificationEvents.BroadcastCircleInviteReceived(invite);
            // });

            _connection.On<ChatMessageEnvelope>(nameof(INotificationHub.NewChatMessageReceived), (message) =>
            {
                _notificationEventHandler.BroadcastNewChatMessageReceived(message);
            });
            
            _connection.On<ChatMessageEnvelope>(nameof(INotificationHub.NewChatMessageSent), (message) =>
            {
                _notificationEventHandler.BroadcastNewChatMessageSent(message);
            });
            

            _connection.On<Message>(nameof(INotificationHub.NewEmailReceived), (message) =>
            {
                _notificationEventHandler.BroadcastNewEmailReceived(message);
            });

            _connection.On<ConnectionRequest>(nameof(INotificationHub.ConnectionRequestReceived), (request) =>
            {
                _notificationEventHandler.BroadcastConnectionRequestReceived(request);
            });

            _connection.On<EstablishConnectionRequest>(nameof(INotificationHub.ConnectionRequestAccepted), (acceptedRequest) =>
            {
                _notificationEventHandler.BroadcastConnectionRequestAccepted(acceptedRequest);
            });

            await _connection.StartAsync();
        }
    }
}