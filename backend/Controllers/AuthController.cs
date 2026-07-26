using AspNet.Security.OAuth.Discord;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizParty.Api.Data;
using QuizParty.Api.Dtos;
using QuizParty.Api.Extensions;

namespace QuizParty.Api.Controllers;

[ApiController]
public class AuthController(QuizPartyDbContext db) : ControllerBase
{
    /// <summary>Point d'entrée pour le bouton "Se connecter avec Discord" côté frontend (navigation complète, pas un appel XHR).</summary>
    [HttpGet("auth/discord/login")]
    public IActionResult LoginWithDiscord()
    {
        return Challenge(new AuthenticationProperties(), DiscordAuthenticationDefaults.AuthenticationScheme);
    }

    [Authorize]
    [HttpGet("api/auth/me")]
    public async Task<ActionResult<CurrentUserDto>> GetCurrentUser()
    {
        var id = User.GetGameMasterId();
        var gameMaster = await db.GameMasters.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id);
        if (gameMaster is null)
        {
            return NotFound();
        }

        return Ok(new CurrentUserDto(gameMaster.Id, gameMaster.Username, gameMaster.AvatarUrl));
    }
}
