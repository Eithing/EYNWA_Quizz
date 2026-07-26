using System.Security.Claims;

namespace QuizParty.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static int GetGameMasterId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.Parse(value ?? throw new InvalidOperationException("Utilisateur non authentifié."));
    }
}
