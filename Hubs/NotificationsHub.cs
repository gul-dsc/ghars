using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GharsPlatform.Hubs;

[Authorize]
public class NotificationsHub : Hub
{
}
