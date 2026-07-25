using Microsoft.AspNetCore.SignalR;

namespace Server.Hubs;

public class QuizHub : Hub
{
    public async Task JoinSession(string sessionCode)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionCode);
    }

    public async Task LeaveSession(string sessionCode)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionCode);
    }
}
