using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QuizParty.Api.Data;
using QuizParty.Api.Dtos;
using QuizParty.Api.Extensions;
using QuizParty.Api.Features.OrderList;
using QuizParty.Api.Features.Qcm;
using QuizParty.Api.Features.Shared;
using QuizParty.Api.Hubs;
using QuizParty.Api.Models;
using QuizParty.Api.Services;

namespace QuizParty.Api.Controllers;

public partial class SessionsController
{
    /// <summary>Lance un tirage aléatoire. Mode "Reveal" : tire et résout immédiatement (pas de phase de
    /// devinette). Modes "GuessWinner"/"GuessRanking" : crée l'état sans tirer, attend les devinettes via
    /// /random-draw/reveal.</summary>
    [Authorize]
    [HttpPost("{id:int}/random-draw/start")]
    public async Task<ActionResult<GameSessionStateDto>> StartRandomDraw(int id, StartRandomDrawRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;

        if (await HasActiveHostToolAsync(session.Id))
        {
            return BadRequest("Un autre outil est déjà actif — ferme-le avant d'en lancer un nouveau.");
        }

        if (!Enum.TryParse<RandomDrawMode>(request.Mode, out var mode))
        {
            return BadRequest("Mode de tirage invalide.");
        }

        if (request.MinValue >= request.MaxValue)
        {
            return BadRequest("La valeur minimale doit être strictement inférieure à la valeur maximale.");
        }

        var (concerned, error) = ResolveConcernedPlayerIds(session, request.PlayerIds, request.TeamIds);
        if (error is not null)
        {
            return BadRequest(error);
        }

        var draw = new RandomDrawState
        {
            SessionId = session.Id,
            Mode = mode,
            Label = request.Label.Trim(),
            MinValue = request.MinValue,
            MaxValue = request.MaxValue,
            ConcernedPlayerIdsJson = JsonSerializer.Serialize(concerned),
            CreatedAt = DateTime.UtcNow
        };

        if (mode == RandomDrawMode.Reveal)
        {
            draw.DrawnValue = Random.Shared.Next(request.MinValue, request.MaxValue + 1);
            draw.IsResolved = true;
        }

        db.RandomDrawStates.Add(draw);
        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    /// <summary>Modes GuessWinner/GuessRanking uniquement : tire la valeur et classe les devinettes déjà
    /// reçues par proximité (égalités groupées au même rang, pas de points — juste un ordre/gagnant).</summary>
    [Authorize]
    [HttpPost("{id:int}/random-draw/reveal")]
    public async Task<ActionResult<GameSessionStateDto>> RevealRandomDraw(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;

        var draw = await db.RandomDrawStates.SingleOrDefaultAsync(r => r.SessionId == session.Id && !r.IsClosed);
        if (draw is null || draw.Mode == RandomDrawMode.Reveal || draw.IsResolved)
        {
            return BadRequest("Aucun tirage en attente de révélation.");
        }

        draw.DrawnValue = Random.Shared.Next(draw.MinValue, draw.MaxValue + 1);
        draw.IsResolved = true;

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [Authorize]
    [HttpPost("{id:int}/random-draw/close")]
    public async Task<ActionResult<GameSessionStateDto>> CloseRandomDraw(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;

        var draw = await db.RandomDrawStates.SingleOrDefaultAsync(r => r.SessionId == session.Id && !r.IsClosed);
        if (draw is null)
        {
            return BadRequest("Aucun tirage actif.");
        }

        draw.IsClosed = true;
        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [Authorize]
    [HttpPost("{id:int}/strawpoll/start")]
    public async Task<ActionResult<GameSessionStateDto>> StartStrawPoll(int id, StartStrawPollRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;

        if (await HasActiveHostToolAsync(session.Id))
        {
            return BadRequest("Un autre outil est déjà actif — ferme-le avant d'en lancer un nouveau.");
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest("Question requise.");
        }

        var options = request.Options.Select(o => o.Trim()).Where(o => o.Length > 0).ToList();
        if (options.Count < 2)
        {
            return BadRequest("Il faut au moins 2 options.");
        }

        var (concerned, error) = ResolveConcernedPlayerIds(session, request.PlayerIds, request.TeamIds);
        if (error is not null)
        {
            return BadRequest(error);
        }

        var optionDtos = options.Select(o => new StrawPollOptionDto(Guid.NewGuid().ToString("N"), o)).ToList();

        var poll = new StrawPollState
        {
            SessionId = session.Id,
            Question = request.Question.Trim(),
            OptionsJson = JsonSerializer.Serialize(optionDtos),
            AllowMultipleVotes = request.AllowMultipleVotes,
            ConcernedPlayerIdsJson = JsonSerializer.Serialize(concerned),
            CreatedAt = DateTime.UtcNow
        };

        db.StrawPollStates.Add(poll);
        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    /// <summary>Révèle les résultats du sondage actif — mêmes principes que ScoreboardVisible : contrôlé
    /// explicitement par l'hôte, aucun décompte n'est exposé aux joueurs (ni à l'hôte via le DTO partagé)
    /// avant cet appel.</summary>
    [Authorize]
    [HttpPost("{id:int}/strawpoll/reveal-results")]
    public async Task<ActionResult<GameSessionStateDto>> RevealStrawPollResults(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;

        var poll = await db.StrawPollStates.SingleOrDefaultAsync(p => p.SessionId == session.Id && !p.IsClosed);
        if (poll is null)
        {
            return BadRequest("Aucun sondage actif.");
        }

        poll.ResultsRevealed = true;
        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [Authorize]
    [HttpPost("{id:int}/strawpoll/close")]
    public async Task<ActionResult<GameSessionStateDto>> CloseStrawPoll(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;

        var poll = await db.StrawPollStates.SingleOrDefaultAsync(p => p.SessionId == session.Id && !p.IsClosed);
        if (poll is null)
        {
            return BadRequest("Aucun sondage actif.");
        }

        poll.IsClosed = true;
        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [AllowAnonymous]
    [HttpPost("by-token/{token}/random-draw/guess")]
    public async Task<ActionResult<GameSessionStateDto>> SubmitRandomDrawGuess(string token, RandomDrawGuessRequest request)
    {
        var loaded = await LoadSessionByToken(token);
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;

        var player = await db.Players.SingleOrDefaultAsync(p => p.SessionId == session.Id && p.ConnectionToken == request.ConnectionToken);
        if (player is null)
        {
            return Unauthorized();
        }

        var draw = await db.RandomDrawStates.SingleOrDefaultAsync(r => r.SessionId == session.Id && !r.IsClosed);
        if (draw is null || draw.Mode == RandomDrawMode.Reveal || draw.IsResolved)
        {
            return BadRequest("Aucun tirage en attente de devinette.");
        }

        var concerned = JsonSerializer.Deserialize<List<int>>(draw.ConcernedPlayerIdsJson, JsonOptions) ?? [];
        if (concerned.Count > 0 && !concerned.Contains(player.Id))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Vous n'êtes pas concerné par ce tirage.");
        }

        if (request.GuessValue < draw.MinValue || request.GuessValue > draw.MaxValue)
        {
            return BadRequest("Devinette hors des bornes du tirage.");
        }

        var existing = await db.RandomDrawGuesses.SingleOrDefaultAsync(g => g.RandomDrawStateId == draw.Id && g.PlayerId == player.Id);
        if (existing is null)
        {
            db.RandomDrawGuesses.Add(new RandomDrawGuess
            {
                RandomDrawStateId = draw.Id,
                PlayerId = player.Id,
                GuessValue = request.GuessValue,
                SubmittedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.GuessValue = request.GuessValue;
            existing.SubmittedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [AllowAnonymous]
    [HttpPost("by-token/{token}/strawpoll/vote")]
    public async Task<ActionResult<GameSessionStateDto>> SubmitStrawPollVote(string token, StrawPollVoteRequest request)
    {
        var loaded = await LoadSessionByToken(token);
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;

        var player = await db.Players.SingleOrDefaultAsync(p => p.SessionId == session.Id && p.ConnectionToken == request.ConnectionToken);
        if (player is null)
        {
            return Unauthorized();
        }

        var poll = await db.StrawPollStates.SingleOrDefaultAsync(p => p.SessionId == session.Id && !p.IsClosed);
        if (poll is null || poll.ResultsRevealed)
        {
            return BadRequest("Aucun sondage en attente de vote.");
        }

        var concerned = JsonSerializer.Deserialize<List<int>>(poll.ConcernedPlayerIdsJson, JsonOptions) ?? [];
        if (concerned.Count > 0 && !concerned.Contains(player.Id))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Vous n'êtes pas concerné par ce sondage.");
        }

        var validOptionIds = (JsonSerializer.Deserialize<List<StrawPollOptionDto>>(poll.OptionsJson, JsonOptions) ?? [])
            .Select(o => o.Id)
            .ToHashSet();
        var selected = request.OptionIds.Distinct().ToList();

        if (selected.Count == 0 || selected.Any(optionId => !validOptionIds.Contains(optionId)))
        {
            return BadRequest("Sélection invalide.");
        }
        if (!poll.AllowMultipleVotes && selected.Count > 1)
        {
            return BadRequest("Ce sondage n'autorise qu'un seul choix.");
        }

        // Autorise à revoter (remplace le vote précédent) plutôt que de rejeter un second appel — plus
        // simple côté joueur qu'un message d'erreur s'il change d'avis avant la révélation des résultats.
        var existingVotes = await db.StrawPollVotes.Where(v => v.StrawPollStateId == poll.Id && v.PlayerId == player.Id).ToListAsync();
        db.StrawPollVotes.RemoveRange(existingVotes);

        foreach (var optionId in selected)
        {
            db.StrawPollVotes.Add(new StrawPollVote
            {
                StrawPollStateId = poll.Id,
                PlayerId = player.Id,
                OptionId = optionId,
                SubmittedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    private async Task<bool> HasActiveHostToolAsync(int sessionId)
    {
        if (await db.RandomDrawStates.AnyAsync(r => r.SessionId == sessionId && !r.IsClosed))
        {
            return true;
        }

        return await db.StrawPollStates.AnyAsync(p => p.SessionId == sessionId && !p.IsClosed);
    }

    /// <summary>Résout la sélection "qui est concerné" d'un outil host (tirage aléatoire, sondage) en une
    /// liste d'IDs joueurs — équipes déjà résolues en joueurs, jamais stockées telles quelles. Contrairement
    /// à ApplyRoundParticipantsAsync, une sélection vide est valide ici (= tout le monde concerné).</summary>
    private (List<int>? PlayerIds, string? Error) ResolveConcernedPlayerIds(GameSession session, List<int> playerIds, List<int> teamIds)
    {
        if (playerIds.Count > 0 && teamIds.Count > 0)
        {
            return (null, "Choisis soit des joueurs, soit des équipes, pas les deux à la fois.");
        }

        var validPlayerIds = session.Players.Select(p => p.Id).ToHashSet();
        if (playerIds.Any(id => !validPlayerIds.Contains(id)))
        {
            return (null, "Joueur introuvable dans cette session.");
        }

        var validTeamIds = session.Teams.Select(t => t.Id).ToHashSet();
        if (teamIds.Any(id => !validTeamIds.Contains(id)))
        {
            return (null, "Équipe introuvable dans cette session.");
        }

        if (teamIds.Count > 0)
        {
            return (session.Players.Where(p => p.TeamId is not null && teamIds.Contains(p.TeamId.Value)).Select(p => p.Id).ToList(), null);
        }

        return (playerIds, null);
    }

    private async Task<RandomDrawStateDto?> BuildActiveRandomDrawDtoAsync(int sessionId)
    {
        var draw = await db.RandomDrawStates
            .Include(r => r.Guesses).ThenInclude(g => g.Player)
            .SingleOrDefaultAsync(r => r.SessionId == sessionId && !r.IsClosed);
        if (draw is null)
        {
            return null;
        }

        var concerned = JsonSerializer.Deserialize<List<int>>(draw.ConcernedPlayerIdsJson, JsonOptions) ?? [];
        var submitted = draw.Guesses.Select(g => g.PlayerId).Distinct().ToList();

        List<RandomDrawResultEntryDto>? results = null;
        if (draw.IsResolved && draw.DrawnValue is not null && draw.Guesses.Count > 0)
        {
            // Classement "olympique" par proximité, même principe que ResolveDeferredScoringAsync
            // (closest-guess) mais sans points : juste un ordre/gagnant.
            var tieGroups = draw.Guesses
                .GroupBy(g => Math.Abs(g.GuessValue - draw.DrawnValue.Value))
                .OrderBy(g => g.Key)
                .ToList();

            results = [];
            var rank = 0;
            foreach (var tieGroup in tieGroups)
            {
                foreach (var guess in tieGroup)
                {
                    results.Add(new RandomDrawResultEntryDto(guess.PlayerId, guess.Player!.Pseudo, guess.GuessValue, rank, rank == 0));
                }
                rank += tieGroup.Count();
            }
        }

        return new RandomDrawStateDto(draw.Id, draw.Mode.ToString(), draw.Label, draw.MinValue, draw.MaxValue, concerned, submitted, draw.IsResolved, draw.DrawnValue, results);
    }

    private async Task<StrawPollStateDto?> BuildActiveStrawPollDtoAsync(int sessionId)
    {
        var poll = await db.StrawPollStates
            .Include(p => p.Votes)
            .SingleOrDefaultAsync(p => p.SessionId == sessionId && !p.IsClosed);
        if (poll is null)
        {
            return null;
        }

        var concerned = JsonSerializer.Deserialize<List<int>>(poll.ConcernedPlayerIdsJson, JsonOptions) ?? [];
        var options = JsonSerializer.Deserialize<List<StrawPollOptionDto>>(poll.OptionsJson, JsonOptions) ?? [];
        var voted = poll.Votes.Select(v => v.PlayerId).Distinct().ToList();

        var results = poll.ResultsRevealed
            ? options.Select(o => new StrawPollResultDto(o.Id, poll.Votes.Count(v => v.OptionId == o.Id))).ToList()
            : null;

        return new StrawPollStateDto(poll.Id, poll.Question, options, poll.AllowMultipleVotes, concerned, voted, poll.ResultsRevealed, results);
    }
}
