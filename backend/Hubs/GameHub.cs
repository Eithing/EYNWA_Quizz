using Microsoft.AspNetCore.SignalR;

namespace QuizParty.Api.Hubs;

/// <summary>Un groupe par session de jeu (nommé par l'InviteToken), section 3 de la spec.</summary>
public class GameHub : Hub
{
    public async Task JoinSession(string inviteToken)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, inviteToken);
    }

    public async Task LeaveSession(string inviteToken)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, inviteToken);
    }
}
