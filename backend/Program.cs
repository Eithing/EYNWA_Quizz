using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using AspNet.Security.OAuth.Discord;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuizParty.Api.Data;
using QuizParty.Api.Features;
using QuizParty.Api.Features.BlindTest;
using QuizParty.Api.Features.ImageGuess;
using QuizParty.Api.Features.Qa;
using QuizParty.Api.Features.Shared;
using QuizParty.Api.Features.Zoom;
using QuizParty.Api.Hubs;
using QuizParty.Api.Models;
using QuizParty.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(); // no-op sauf si réellement lancé comme service Windows installé

const string ClientCorsPolicy = "ClientCorsPolicy";
const string ExternalCookieScheme = "ExternalCookie";

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<IQuizFeature, ZoomImageFeature>();
builder.Services.AddSingleton<IQuizFeature, QaTextFeature>();
builder.Services.AddSingleton<IQuizFeature, BlindTestFeature>();
builder.Services.AddSingleton<IQuizFeature, ImageGuessFeature>();
builder.Services.AddSingleton<FeatureRegistry>();
builder.Services.AddSingleton<IFeatureEngine, ZoomImageEngine>();
builder.Services.AddSingleton<IFeatureEngine, QaEngine>();
builder.Services.AddSingleton<IFeatureEngine, BlindTestEngine>();
builder.Services.AddSingleton<IFeatureEngine, ImageGuessEngine>();
builder.Services.AddSingleton<FeatureEngineRegistry>();

builder.Services.AddDbContext<QuizPartyDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=quizparty.db"));

var jwtSection = builder.Configuration.GetSection("Jwt");
var frontendBaseUrl = builder.Configuration["Frontend:BaseUrl"] ?? "http://localhost:4200";

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!))
        };
    })
    // Scheme technique utilisé uniquement pour faire transiter l'état de corrélation OAuth
    // pendant l'aller-retour vers Discord (jamais utilisé pour l'auth applicative elle-même).
    .AddCookie(ExternalCookieScheme, options =>
    {
        options.Cookie.Name = "quizparty.external";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddDiscord(options =>
    {
        options.ClientId = builder.Configuration["Discord:ClientId"] ?? string.Empty;
        options.ClientSecret = builder.Configuration["Discord:ClientSecret"] ?? string.Empty;
        options.CallbackPath = "/auth/discord/callback";
        options.SignInScheme = ExternalCookieScheme;

        options.Events.OnCreatingTicket = context =>
        {
            var discordId = context.User.GetProperty("id").GetString()!;
            var username = context.User.GetProperty("username").GetString()!;
            var avatarUrl = context.User.TryGetProperty("avatar", out var avatarProp) && avatarProp.GetString() is { } hash
                ? $"https://cdn.discordapp.com/avatars/{discordId}/{hash}.png"
                : null;

            context.Identity!.AddClaim(new Claim("discord_id", discordId));
            context.Identity.AddClaim(new Claim("discord_username", username));
            if (avatarUrl is not null)
            {
                context.Identity.AddClaim(new Claim("discord_avatar_url", avatarUrl));
            }

            return Task.CompletedTask;
        };

        options.Events.OnTicketReceived = async context =>
        {
            var principal = context.Principal!;
            var discordId = principal.FindFirstValue("discord_id")!;
            var username = principal.FindFirstValue("discord_username")!;
            var avatarUrl = principal.FindFirstValue("discord_avatar_url");

            var db = context.HttpContext.RequestServices.GetRequiredService<QuizPartyDbContext>();
            var gameMaster = await db.GameMasters.SingleOrDefaultAsync(g => g.DiscordId == discordId);

            if (gameMaster is null)
            {
                gameMaster = new GameMaster
                {
                    DiscordId = discordId,
                    Username = username,
                    AvatarUrl = avatarUrl,
                    CreatedAt = DateTime.UtcNow
                };
                db.GameMasters.Add(gameMaster);
            }
            else
            {
                gameMaster.Username = username;
                gameMaster.AvatarUrl = avatarUrl;
            }

            await db.SaveChangesAsync();

            var tokenService = context.HttpContext.RequestServices.GetRequiredService<JwtTokenService>();
            var jwt = tokenService.GenerateToken(gameMaster);

            context.Response.Redirect($"{frontendBaseUrl}/auth/callback?token={Uri.EscapeDataString(jwt)}");
            context.HandleResponse();
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy(ClientCorsPolicy, policy =>
    {
        // AllowCredentials est requis par le client SignalR pour le hub temps réel
        // (il envoie ses requêtes de négociation en mode "credentials include" même sans cookie applicatif) ;
        // WithOrigins (une origine explicite, pas AllowAnyOrigin) est donc obligatoire en contrepartie.
        policy.WithOrigins(frontendBaseUrl)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Derrière cloudflared/Cloudflare : la requête arrive en HTTP en local (TLS déjà terminé par
// l'edge Cloudflare), donc sans ça, Request.Scheme/Host restent sur des valeurs locales
// (ex: "localhost:5100") au lieu du vrai hostname public — ça casse le redirect_uri envoyé
// à Discord OAuth et déclenche une redirection HTTPS interne incorrecte.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<QuizPartyDbContext>().Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors(ClientCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GameHub>("/hubs/game");
app.MapFallbackToFile("index.html"); // routing côté client Angular en prod (wwwroot alimenté au déploiement)

app.Run();
