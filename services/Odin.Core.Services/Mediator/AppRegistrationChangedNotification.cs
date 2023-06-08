using System;
using MediatR;
using Youverse.Core.Services.Authorization.Apps;

namespace Youverse.Core.Services.Mediator;

public class AppRegistrationChangedNotification : EventArgs, INotification
{
    public AppRegistration NewAppRegistration { get; set; }
    public AppRegistration OldAppRegistration { get; set; }
}