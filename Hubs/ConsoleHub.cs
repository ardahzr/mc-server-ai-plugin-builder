using Microsoft.AspNetCore.SignalR;

namespace McPanel.Hubs;

public class ConsoleHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}
