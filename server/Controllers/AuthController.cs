using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Dtos;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(QuizDbContext db, JwtTokenService tokenService) : ControllerBase
{
    private readonly PasswordHasher<GameMaster> _passwordHasher = new();

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var username = request.Username.Trim();

        if (string.IsNullOrWhiteSpace(username) || request.Password.Length < 6)
        {
            return BadRequest("Pseudo requis, mot de passe d'au moins 6 caractères.");
        }

        if (await db.GameMasters.AnyAsync(g => g.Username == username))
        {
            return Conflict("Ce pseudo est déjà pris.");
        }

        var gameMaster = new GameMaster
        {
            Username = username,
            PasswordHash = string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        };
        gameMaster.PasswordHash = _passwordHasher.HashPassword(gameMaster, request.Password);

        db.GameMasters.Add(gameMaster);
        await db.SaveChangesAsync();

        var token = tokenService.GenerateToken(gameMaster);
        return Ok(new AuthResponse(token, gameMaster.Username));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var gameMaster = await db.GameMasters.SingleOrDefaultAsync(g => g.Username == request.Username.Trim());
        if (gameMaster is null)
        {
            return Unauthorized("Identifiants invalides.");
        }

        var result = _passwordHasher.VerifyHashedPassword(gameMaster, gameMaster.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return Unauthorized("Identifiants invalides.");
        }

        var token = tokenService.GenerateToken(gameMaster);
        return Ok(new AuthResponse(token, gameMaster.Username));
    }
}
