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

[ApiController]
[Route("api/sessions")]
public partial class SessionsController(QuizPartyDbContext db, FeatureEngineRegistry engineRegistry, IHubContext<GameHub> hub) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Authorize]
    [HttpPost("start/{quizId:int}")]
    public async Task<ActionResult<GameSessionStateDto>> StartSession(int quizId)
    {
        var ownerId = User.GetGameMasterId();

        var quiz = await db.Quizzes
            .Include(q => q.Rounds).ThenInclude(r => r.Questions)
            .SingleOrDefaultAsync(q => q.Id == quizId && q.OwnerId == ownerId);

        if (quiz is null)
        {
            return NotFound();
        }

        if (quiz.Rounds.Count == 0)
        {
            return BadRequest("Ce quiz n'a aucune manche.");
        }

        var session = new GameSession
        {
            QuizId = quizId,
            InviteToken = await GenerateUniqueInviteToken(),
            Status = GameSessionStatus.Lobby,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(12),
            CurrentRoundIndex = -1,
            CurrentQuestionIndex = -1
        };

        db.GameSessions.Add(session);
        await db.SaveChangesAsync();

        return Ok(await BuildStateDto(session, quiz));
    }

    [Authorize]
    [HttpGet("{id:int}/state")]
    public async Task<ActionResult<GameSessionStateDto>> GetStateAsGm(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        await CheckAutoAdvance(quiz, session);

        return Ok(await BuildStateDto(session, quiz));
    }

    [Authorize]
    [HttpPost("{id:int}/begin")]
    public async Task<ActionResult<GameSessionStateDto>> Begin(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        if (session.Status != GameSessionStatus.Lobby)
        {
            return BadRequest("La session a déjà démarré.");
        }

        await EnterRoundAsync(TopLevelRounds(quiz), session, 0);

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    /// <summary>Désigne, pour la manche restreinte en attente, la sélection de joueurs et/ou d'équipes qui
    /// participent (les autres deviennent spectateurs). Sélectionner au moins une équipe active le mode
    /// équipe pour cette manche (les points vont dans le pot plutôt que dans le score perso).</summary>
    [Authorize]
    [HttpPost("{id:int}/round-participants")]
    public async Task<ActionResult<GameSessionStateDto>> SetRoundParticipants(int id, SetRoundParticipantsRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        if (session.Status != GameSessionStatus.AwaitingParticipants)
        {
            return BadRequest("La session n'attend pas de désignation de participants.");
        }

        var validationError = await ApplyRoundParticipantsAsync(session, request.PlayerIds, request.TeamIds);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        session.Status = GameSessionStatus.Running;
        session.CurrentQuestionStartedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [Authorize]
    [HttpPost("{id:int}/teams")]
    public async Task<ActionResult<GameSessionStateDto>> SetTeams(int id, SetTeamsRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;

        var existingTeams = await db.Teams.Where(t => t.SessionId == id).ToListAsync();
        db.Teams.RemoveRange(existingTeams); // les joueurs pointant dessus repassent à TeamId=null (SetNull)

        foreach (var teamRequest in request.Teams)
        {
            if (string.IsNullOrWhiteSpace(teamRequest.Name))
            {
                return BadRequest("Chaque équipe doit avoir un nom.");
            }

            var team = new Team { SessionId = id, Name = teamRequest.Name.Trim() };
            db.Teams.Add(team);

            var players = session.Players.Where(p => teamRequest.PlayerIds.Contains(p.Id)).ToList();
            foreach (var player in players)
            {
                player.Team = team;
            }
        }

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    /// <summary>Bascule le mode équipe pour la manche en cours (sans passer par une sélection de
    /// participants restreinte) : les points gagnés à partir de maintenant vont dans le pot d'équipe du
    /// joueur qui répond plutôt que dans son score perso.</summary>
    [Authorize]
    [HttpPost("{id:int}/team-scoring")]
    public async Task<ActionResult<GameSessionStateDto>> SetTeamScoring(int id, SetTeamScoringRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        session.TeamScoringEnabled = request.Enabled;

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    /// <summary>Ferme le palier d'attente posé au démarrage d'une manche quand des équipes existent : le
    /// GM tranche le mode équipe avant que le minuteur ne démarre, contrairement à /team-scoring qui bascule
    /// le mode en cours de manche une fois les joueurs déjà en train de répondre.</summary>
    [Authorize]
    [HttpPost("{id:int}/round-team-mode")]
    public async Task<ActionResult<GameSessionStateDto>> SetRoundTeamMode(int id, SetTeamScoringRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        if (session.Status != GameSessionStatus.AwaitingTeamMode)
        {
            return BadRequest("La session n'attend pas de choix du mode équipe.");
        }

        session.TeamScoringEnabled = request.Enabled;
        session.Status = GameSessionStatus.Running;
        session.CurrentQuestionStartedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [Authorize]
    [HttpGet("{id:int}/pending-round-preview")]
    public async Task<ActionResult<RoundPreviewDto>> GetPendingRoundPreview(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        if (session.Status is not (GameSessionStatus.AwaitingParticipants or GameSessionStatus.AwaitingTeamMode))
        {
            return BadRequest("La session n'attend pas de désignation de participants.");
        }

        var (round, question) = GetCurrentRoundAndQuestion(quiz, session);
        if (round is null)
        {
            return NotFound();
        }

        return Ok(new RoundPreviewDto(round.Title, round.FeatureTypeKey, question?.PayloadJson));
    }

    [Authorize]
    [HttpPost("{id:int}/pause")]
    public async Task<ActionResult<GameSessionStateDto>> Pause(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        if (session.Status != GameSessionStatus.Running)
        {
            return BadRequest("La session n'est pas en cours.");
        }

        session.Status = GameSessionStatus.Paused;
        session.PausedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [Authorize]
    [HttpPost("{id:int}/resume")]
    public async Task<ActionResult<GameSessionStateDto>> Resume(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        if (session.Status != GameSessionStatus.Paused || session.PausedAt is null)
        {
            return BadRequest("La session n'est pas en pause.");
        }

        var pausedDuration = DateTime.UtcNow - session.PausedAt.Value;
        session.CurrentQuestionStartedAt = session.CurrentQuestionStartedAt!.Value + pausedDuration;
        session.PausedAt = null;
        session.Status = GameSessionStatus.Running;

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [Authorize]
    [HttpPost("{id:int}/next")]
    public async Task<ActionResult<GameSessionStateDto>> Next(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        if (session.Status is not (GameSessionStatus.Running or GameSessionStatus.Paused or GameSessionStatus.RoundIntermission or GameSessionStatus.ChoosingTheme))
        {
            return BadRequest("La session n'est pas en cours.");
        }

        // Action explicite du GM : autorisée à franchir la frontière d'une manche, y compris pour sortir
        // d'une manche à thèmes sans avoir joué tous les thèmes (contrairement à l'auto-advance, qui doit
        // toujours s'arrêter en fin de manche).
        await AdvanceToNextQuestionAsync(quiz, session, crossRoundBoundary: true);

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [Authorize]
    [HttpPost("{id:int}/scoreboard")]
    public async Task<ActionResult<GameSessionStateDto>> SetScoreboardVisible(int id, SetScoreboardVisibleRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        session.ScoreboardVisible = request.Visible;

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [AllowAnonymous]
    [HttpGet("by-token/{token}")]
    public async Task<ActionResult<GameSessionStateDto>> GetPublicState(string token)
    {
        var loaded = await LoadSessionByToken(token);
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        await CheckAutoAdvance(quiz, session);

        return Ok(await BuildStateDto(session, quiz));
    }

    [AllowAnonymous]
    [HttpPost("by-token/{token}/join")]
    public async Task<ActionResult<JoinSessionResponse>> Join(string token, JoinSessionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Pseudo))
        {
            return BadRequest("Pseudo requis.");
        }

        var loaded = await LoadSessionByToken(token);
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, _) = loaded.Value;
        if (session.Status == GameSessionStatus.Finished)
        {
            return BadRequest("Cette session est terminée.");
        }

        var player = new Player
        {
            SessionId = session.Id,
            Pseudo = request.Pseudo.Trim(),
            ConnectionToken = Guid.NewGuid(),
            JoinedAt = DateTime.UtcNow
        };

        db.Players.Add(player);
        await db.SaveChangesAsync();

        await hub.Clients.Group(session.InviteToken).SendAsync("PlayerJoined", new PlayerDto(player.Id, player.Pseudo, 0, null, 0, 0));

        return Ok(new JoinSessionResponse(player.Id, player.ConnectionToken, session.Id));
    }

    private async Task<(GameSession session, Quiz quiz)?> LoadOwnedSession(int sessionId, int ownerId)
    {
        var session = await db.GameSessions
            .Include(s => s.Players)
            .Include(s => s.Teams)
            .SingleOrDefaultAsync(s => s.Id == sessionId);
        if (session is null)
        {
            return null;
        }

        var quiz = await db.Quizzes
            .Include(q => q.Rounds).ThenInclude(r => r.Questions)
            .Include(q => q.Rounds).ThenInclude(r => r.SubRounds).ThenInclude(sr => sr.Questions)
            .SingleOrDefaultAsync(q => q.Id == session.QuizId && q.OwnerId == ownerId);

        return quiz is null ? null : (session, quiz);
    }

    private async Task<(GameSession session, Quiz quiz)?> LoadSessionByToken(string token)
    {
        var session = await db.GameSessions
            .Include(s => s.Players)
            .Include(s => s.Teams)
            .SingleOrDefaultAsync(s => s.InviteToken == token);
        // Lien d'invitation expiré : traité comme introuvable pour les joueurs (nouveaux arrivants
        // comme sessions déjà en cours de sondage) — n'affecte pas l'accès du GM, qui passe par
        // LoadOwnedSession et peut toujours superviser/clôturer une session au-delà de ce délai.
        if (session is null || session.ExpiresAt < DateTime.UtcNow)
        {
            return null;
        }

        var quiz = await db.Quizzes
            .Include(q => q.Rounds).ThenInclude(r => r.Questions)
            .Include(q => q.Rounds).ThenInclude(r => r.SubRounds).ThenInclude(sr => sr.Questions)
            .SingleOrDefaultAsync(q => q.Id == session.QuizId);

        return quiz is null ? null : (session, quiz);
    }

    private async Task<string> GenerateUniqueInviteToken()
    {
        string token;
        do
        {
            token = InviteTokenGenerator.Generate();
        } while (await db.GameSessions.AnyAsync(s => s.InviteToken == token));

        return token;
    }

    private async Task CheckAutoAdvance(Quiz quiz, GameSession session)
    {
        if (session.Status != GameSessionStatus.Running || session.CurrentQuestionStartedAt is null)
        {
            return;
        }

        var (round, question) = GetCurrentRoundAndQuestion(quiz, session);
        if (round is null || question is null)
        {
            return;
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);

        // Si tout le monde a déjà trouvé, inutile de faire attendre les joueurs jusqu'au bout du
        // "suspense" (paliers de zoom, minuteur…) : on saute directement à la dernière étape utile,
        // qu'on laisse s'écouler normalement avant de passer à la question suivante.
        var dirty = await FastForwardIfAllPlayersAnswered(session, round, question, engine);

        // Feature à résolution différée (closest-guess) : le classement ne peut être calculé qu'une fois
        // la fenêtre fermée. Doit tourner AVANT le calcul de allAnswered ci-dessous, sinon on avancerait à
        // la question suivante sans jamais avoir noté les estimations en attente.
        dirty |= await ResolveDeferredScoringIfDueAsync(session, round, question, engine);

        // Feature à finalisation indépendante (order-list) : chaque brouillon en attente se note pour
        // son propre compte dès la fermeture de la fenêtre, sans attendre un classement collectif —
        // permet au joueur de voir son résultat même s'il n'a jamais cliqué "Valider".
        dirty |= await FinalizeIndependentPendingAnswersIfDueAsync(session, round, question, engine);

        // Joker Copier/coller : une fois la fenêtre fermée, les assignations en attente sur cette
        // question copient la réponse (déjà finalisée ci-dessus si besoin) du joueur ciblé.
        dirty |= await ResolveCopyPasteAssignmentsIfDueAsync(session, round, question, engine);

        var state = engine.ComputeState(round.ConfigJson, session.CurrentQuestionStartedAt!.Value, session.PausedAt, DateTime.UtcNow);
        var allAnswered = await AllPlayersAnsweredCorrectly(session, round, question);

        // Une fois que tout le monde a trouvé, la question se termine automatiquement même si
        // l'auto-advance n'est pas coché sur la manche : il n'y a plus personne pour répondre.
        if (state.ShouldAutoAdvance || (allAnswered && !state.IsAnswerWindowOpen))
        {
            // Ne franchit jamais une frontière de manche tout seul, seul le GM en décide.
            await AdvanceToNextQuestionAsync(quiz, session, crossRoundBoundary: false);
            dirty = true;
        }

        if (dirty)
        {
            await db.SaveChangesAsync();
            await BroadcastState(session, quiz);
        }
    }

    private async Task AdvanceToNextQuestionAsync(Quiz quiz, GameSession session, bool crossRoundBoundary)
    {
        // Filet de sécurité pour une feature à finalisation indépendante (order-list) : si le GM clique
        // "Suivant" avant qu'aucun poll n'ait eu l'occasion de résoudre les brouillons restants (voir
        // FinalizeIndependentPendingAnswersIfDueAsync, déclenché normalement depuis CheckAutoAdvance), on
        // ne quitte jamais la question en laissant des réponses définitivement bloquées à IsCorrect==null.
        var (leavingRound, leavingQuestion) = GetCurrentRoundAndQuestion(quiz, session);
        if (leavingRound is not null && leavingQuestion is not null)
        {
            var leavingEngine = engineRegistry.Get(leavingRound.FeatureTypeKey);
            if (leavingEngine.FinalizesPendingAnswersOnAdvance(leavingRound.ConfigJson))
            {
                await FinalizeIndependentPendingAnswersAsync(session, leavingRound, leavingQuestion, leavingEngine);
            }

            // Même filet de sécurité pour le joker Copier/coller : si le GM clique "Suivant" avant qu'un
            // poll n'ait eu l'occasion de résoudre une assignation en attente, elle est appliquée ici avec
            // les réponses telles qu'elles sont au moment de quitter la question (voir ResolveCopyPasteAssignmentsIfDueAsync).
            await ResolveCopyPasteAssignmentsAsync(session, leavingRound, leavingQuestion, leavingEngine);
        }

        var rounds = TopLevelRounds(quiz);

        // Sous-manche (thème) active : sa propre liste de questions gouverne l'avancement, pas celle de
        // la manche à thèmes parente (qui n'en porte jamais directement). Une fois épuisée, retour au
        // plateau plutôt qu'à la manche suivante — c'est le GM qui décide quand quitter la manche à thèmes.
        if (session.CurrentThemeSubRoundId is not null)
        {
            var parentRound = rounds.ElementAtOrDefault(session.CurrentRoundIndex);
            var subRound = parentRound?.SubRounds.SingleOrDefault(sr => sr.Id == session.CurrentThemeSubRoundId);
            var subQuestionCount = subRound?.Questions.Count ?? 0;

            if (session.CurrentQuestionIndex + 1 < subQuestionCount)
            {
                session.CurrentQuestionIndex++;
                session.Status = GameSessionStatus.Running;
                session.CurrentQuestionStartedAt = DateTime.UtcNow;
                session.PausedAt = null;
                session.CurrentBuzzHolderPlayerId = null; ResetPerQuestionJokerEffects(session);
                return;
            }

            var themeState = await db.ThemeStates
                .SingleOrDefaultAsync(t => t.SessionId == session.Id && t.SubRoundId == session.CurrentThemeSubRoundId);
            if (themeState is not null)
            {
                themeState.Resolution = ThemeResolution.Played;
            }

            session.CurrentThemeSubRoundId = null;
            session.CurrentQuestionIndex = -1;
            session.Status = GameSessionStatus.ChoosingTheme;
            session.CurrentQuestionStartedAt = null;
            session.PausedAt = null;
            session.CurrentBuzzHolderPlayerId = null; ResetPerQuestionJokerEffects(session);
            return;
        }

        var currentRound = session.CurrentRoundIndex >= 0 && session.CurrentRoundIndex < rounds.Count
            ? rounds[session.CurrentRoundIndex]
            : null;
        // Une manche à thèmes ne porte jamais de questions directement (Questions.Count vaut toujours 0
        // dessus) : elle est donc naturellement traitée comme "immédiatement épuisée" ci-dessous, exactement
        // comme une manche normale sans questions — pas de branche spéciale nécessaire.
        var questionCount = currentRound?.Questions.Count ?? 0;

        var hasNextRound = session.CurrentRoundIndex + 1 < rounds.Count;

        if (session.CurrentQuestionIndex + 1 < questionCount)
        {
            session.CurrentQuestionIndex++;
        }
        else if (hasNextRound && !crossRoundBoundary)
        {
            // Manche terminée mais le GM n'a pas encore choisi de continuer : on s'arrête et on attend
            // (contrairement à la fin de la dernière manche, qui termine la partie sans intervention).
            session.Status = GameSessionStatus.RoundIntermission;
            session.CurrentQuestionStartedAt = null;
            session.PausedAt = null;
            session.CurrentBuzzHolderPlayerId = null; ResetPerQuestionJokerEffects(session);
            return;
        }
        else if (hasNextRound)
        {
            await EnterRoundAsync(rounds, session, session.CurrentRoundIndex + 1);
            return;
        }
        else
        {
            session.Status = GameSessionStatus.Finished;
            session.CurrentQuestionStartedAt = null;
            return;
        }

        // "À quoi pense l'autre" : chaque question désigne son propre répondant, jamais réutilisé d'une
        // question à l'autre — retour à AwaitingAnswerer plutôt que de repartir directement sur Running.
        if (currentRound?.FeatureTypeKey == "partner-guess")
        {
            session.Status = GameSessionStatus.AwaitingAnswerer;
            session.CurrentQuestionStartedAt = null;
            session.PausedAt = null;
            session.CurrentBuzzHolderPlayerId = null; ResetPerQuestionJokerEffects(session);
            session.CurrentAnswererPlayerId = null;

            var staleParticipants = await db.RoundParticipants.Where(rp => rp.SessionId == session.Id).ToListAsync();
            db.RoundParticipants.RemoveRange(staleParticipants);
            return;
        }

        session.Status = GameSessionStatus.Running;
        session.CurrentQuestionStartedAt = DateTime.UtcNow;
        session.PausedAt = null;
        session.CurrentBuzzHolderPlayerId = null; ResetPerQuestionJokerEffects(session);
    }

    /// <summary>Positionne la session au début d'une manche : démarre directement si elle est libre, s'arrête
    /// en AwaitingParticipants si elle est restreinte (Round.RestrictsParticipants) en attendant que le GM
    /// désigne les participants, ou passe en ChoosingTheme si c'est une manche à thèmes (le plateau est
    /// affiché, chaque sous-manche repart d'un état vierge : non révélée, en attente).</summary>
    private async Task EnterRoundAsync(List<Round> rounds, GameSession session, int roundIndex)
    {
        session.CurrentRoundIndex = roundIndex;
        session.CurrentBuzzHolderPlayerId = null; ResetPerQuestionJokerEffects(session);
        session.PausedAt = null;
        session.TeamScoringEnabled = false;
        session.CurrentThemeSubRoundId = null;
        session.CurrentAnswererPlayerId = null;

        var existingParticipants = await db.RoundParticipants.Where(rp => rp.SessionId == session.Id).ToListAsync();
        db.RoundParticipants.RemoveRange(existingParticipants);

        var round = rounds[roundIndex];

        if (round.FeatureTypeKey == "partner-guess")
        {
            // Pas de notion de RestrictsParticipants ici : chaque question désigne son propre répondant,
            // le GM le choisit avant même que le minuteur ne démarre (voir SetPartnerGuessAnswerer).
            session.CurrentQuestionIndex = 0;
            session.Status = GameSessionStatus.AwaitingAnswerer;
            session.CurrentQuestionStartedAt = null;
            return;
        }

        if (round.IsThemePicker)
        {
            session.CurrentQuestionIndex = -1;
            session.Status = GameSessionStatus.ChoosingTheme;
            session.CurrentQuestionStartedAt = null;

            var subRoundIds = round.SubRounds.Select(sr => sr.Id).ToList();
            var staleThemeStates = await db.ThemeStates
                .Where(t => t.SessionId == session.Id && subRoundIds.Contains(t.SubRoundId))
                .ToListAsync();
            db.ThemeStates.RemoveRange(staleThemeStates);

            foreach (var sub in round.SubRounds)
            {
                db.ThemeStates.Add(new ThemeState { SessionId = session.Id, SubRoundId = sub.Id });
            }

            return;
        }

        session.CurrentQuestionIndex = 0;

        if (round.RestrictsParticipants)
        {
            // La sélection de participants (ApplyRoundParticipantsAsync) couvre déjà le choix du mode
            // équipe pour cette manche (sélectionner une équipe l'active) : pas besoin du palier AwaitingTeamMode.
            session.Status = GameSessionStatus.AwaitingParticipants;
            session.CurrentQuestionStartedAt = null;
        }
        else if (session.Teams.Count > 0)
        {
            session.Status = GameSessionStatus.AwaitingTeamMode;
            session.CurrentQuestionStartedAt = null;
        }
        else
        {
            session.Status = GameSessionStatus.Running;
            session.CurrentQuestionStartedAt = DateTime.UtcNow;
        }
    }

    /// <summary>Enregistre la sélection de participants du GM pour la manche restreinte en attente (validation
    /// incluse) — utilisé à la fois par /round-participants et par le choix d'une sous-manche à thèmes.</summary>
    private async Task<string?> ApplyRoundParticipantsAsync(GameSession session, List<int> playerIds, List<int> teamIds)
    {
        if (playerIds.Count == 0 && teamIds.Count == 0)
        {
            return "Sélectionne au moins un joueur ou une équipe.";
        }

        if (playerIds.Count > 0 && teamIds.Count > 0)
        {
            return "Choisis soit des joueurs, soit des équipes, pas les deux à la fois.";
        }

        var validPlayerIds = session.Players.Select(p => p.Id).ToHashSet();
        if (playerIds.Any(id => !validPlayerIds.Contains(id)))
        {
            return "Joueur introuvable dans cette session.";
        }

        var validTeamIds = await db.Teams.Where(t => t.SessionId == session.Id).Select(t => t.Id).ToListAsync();
        if (teamIds.Any(id => !validTeamIds.Contains(id)))
        {
            return "Équipe introuvable dans cette session.";
        }

        var existing = await db.RoundParticipants.Where(rp => rp.SessionId == session.Id).ToListAsync();
        db.RoundParticipants.RemoveRange(existing);

        foreach (var playerId in playerIds)
        {
            db.RoundParticipants.Add(new RoundParticipant { SessionId = session.Id, PlayerId = playerId });
        }

        foreach (var teamId in teamIds)
        {
            db.RoundParticipants.Add(new RoundParticipant { SessionId = session.Id, TeamId = teamId });
        }

        // Choisir une/des équipe(s) comme participants active automatiquement le mode équipe pour la
        // manche (cf. section "à vous de choisir" du cahier des charges) ; en mode joueurs, on n'écrase
        // pas un choix explicite déjà fait via /team-scoring.
        if (teamIds.Count > 0)
        {
            session.TeamScoringEnabled = true;
        }

        return null;
    }

    /// <summary>Liste des joueurs autorisés à jouer la manche courante : tout le monde si elle n'est pas
    /// restreinte, sinon la sélection du GM (joueurs directs + membres des équipes désignées).</summary>
    private async Task<List<int>> GetEligiblePlayerIdsAsync(GameSession session, Round round)
    {
        // "À quoi pense l'autre" est toujours restreinte (au répondant en phase 1, aux devineurs désignés
        // en phase 2) même si Round.RestrictsParticipants n'a pas été coché à l'édition — la restriction
        // se joue entièrement en direct, question par question. Un thème (sous-manche) est toujours
        // restreint lui aussi : ChooseTheme impose systématiquement une désignation de participants avant
        // de le lancer, indépendamment de RestrictsParticipants qui n'est même pas exposé à l'édition pour
        // les sous-manches — sans ce cas particulier, la sélection du GM était enregistrée mais totalement
        // ignorée, tous les joueurs restaient éligibles et pouvaient marquer des points.
        var isThemeSubRound = round.ParentRoundId is not null;
        if (!round.RestrictsParticipants && round.FeatureTypeKey != "partner-guess" && !isThemeSubRound)
        {
            return session.Players.Select(p => p.Id).ToList();
        }

        var participants = await db.RoundParticipants.Where(rp => rp.SessionId == session.Id).ToListAsync();
        var directPlayerIds = participants.Where(p => p.PlayerId is not null).Select(p => p.PlayerId!.Value);
        var teamIds = participants.Where(p => p.TeamId is not null).Select(p => p.TeamId!.Value).ToHashSet();
        var teamPlayerIds = session.Players.Where(p => p.TeamId is not null && teamIds.Contains(p.TeamId.Value)).Select(p => p.Id);

        return directPlayerIds.Concat(teamPlayerIds).Distinct().ToList();
    }

    private static List<Round> TopLevelRounds(Quiz quiz) =>
        quiz.Rounds.Where(r => r.ParentRoundId == null).OrderBy(r => r.Order).ToList();

    private static (Round? round, Question? question) GetCurrentRoundAndQuestion(Quiz quiz, GameSession session)
    {
        var rounds = TopLevelRounds(quiz);
        if (session.CurrentRoundIndex < 0 || session.CurrentRoundIndex >= rounds.Count)
        {
            return (null, null);
        }

        var round = rounds[session.CurrentRoundIndex];

        if (round.IsThemePicker)
        {
            if (session.CurrentThemeSubRoundId is null)
            {
                // Plateau de thèmes affiché, aucun thème choisi pour l'instant : pas de question active.
                return (round, null);
            }

            var subRound = round.SubRounds.SingleOrDefault(sr => sr.Id == session.CurrentThemeSubRoundId);
            if (subRound is null)
            {
                return (null, null);
            }

            round = subRound;
        }

        var questions = round.Questions.OrderBy(q => q.Order).ToList();
        if (session.CurrentQuestionIndex < 0 || session.CurrentQuestionIndex >= questions.Count)
        {
            return (round, null);
        }

        return (round, questions[session.CurrentQuestionIndex]);
    }

    private async Task BroadcastState(GameSession session, Quiz quiz)
    {
        await hub.Clients.Group(session.InviteToken).SendAsync("StateChanged", await BuildStateDto(session, quiz));
    }

    private async Task<GameSessionStateDto> BuildStateDto(GameSession session, Quiz quiz)
    {
        var scores = await ComputeAllScores(session.Id);
        var teamScores = await ComputeAllTeamScores(session.Id);

        var teams = session.Teams
            .Select(t => new TeamDto(
                t.Id, t.Name,
                session.Players.Where(p => p.TeamId == t.Id).Select(p => p.Id).ToList(),
                teamScores.GetValueOrDefault(t.Id)))
            .ToList();

        // Classement sur le total (perso + équipe) : c'est le score final tel que décrit dans le cahier
        // des charges, pas seulement la contribution individuelle.
        var players = session.Players
            .Select(p =>
            {
                var score = scores.GetValueOrDefault(p.Id);
                var teamScore = p.TeamId is null ? 0 : teamScores.GetValueOrDefault(p.TeamId.Value);
                return new PlayerDto(p.Id, p.Pseudo, score, p.TeamId, teamScore, score + teamScore);
            })
            .OrderByDescending(p => p.TotalScore)
            .ToList();

        var participants = await db.RoundParticipants.Where(rp => rp.SessionId == session.Id).ToListAsync();
        var participantPlayerIds = participants.Where(p => p.PlayerId is not null).Select(p => p.PlayerId!.Value).ToList();
        var participantTeamIds = participants.Where(p => p.TeamId is not null).Select(p => p.TeamId!.Value).ToList();

        var buzzHolderPseudo = session.CurrentBuzzHolderPlayerId is null
            ? null
            : session.Players.SingleOrDefault(p => p.Id == session.CurrentBuzzHolderPlayerId)?.Pseudo;

        var topLevelRounds = TopLevelRounds(quiz);
        var currentTopRound = session.CurrentRoundIndex >= 0 && session.CurrentRoundIndex < topLevelRounds.Count
            ? topLevelRounds[session.CurrentRoundIndex]
            : null;

        List<ThemeBoardEntryDto>? themeBoard = null;
        if (currentTopRound?.IsThemePicker == true)
        {
            var subRoundIds = currentTopRound.SubRounds.Select(sr => sr.Id).ToList();
            var themeStates = await db.ThemeStates
                .Where(t => t.SessionId == session.Id && subRoundIds.Contains(t.SubRoundId))
                .ToListAsync();

            themeBoard = currentTopRound.SubRounds
                .OrderBy(sr => sr.Order)
                .Select(sr =>
                {
                    var state = themeStates.SingleOrDefault(t => t.SubRoundId == sr.Id);
                    return new ThemeBoardEntryDto(sr.Id, sr.Title, state?.IsRevealed ?? false, (state?.Resolution ?? ThemeResolution.Pending).ToString());
                })
                .ToList();
        }

        var answererPseudo = session.CurrentAnswererPlayerId is null
            ? null
            : session.Players.SingleOrDefault(p => p.Id == session.CurrentAnswererPlayerId)?.Pseudo;

        var activeRandomDraw = await BuildActiveRandomDrawDtoAsync(session.Id);
        var activeStrawPoll = await BuildActiveStrawPollDtoAsync(session.Id);

        var jokerGrants = await db.JokerGrants.Where(g => g.SessionId == session.Id)
            .Select(g => new JokerGrantDto(g.Id, g.Type.ToString(), g.PlayerId, g.TeamId, g.Charges))
            .ToListAsync();

        return new GameSessionStateDto(
            session.Id, session.InviteToken, quiz.Title, session.Status,
            session.CurrentRoundIndex, session.CurrentQuestionIndex, topLevelRounds.Count, session.ScoreboardVisible,
            participantPlayerIds, participantTeamIds, session.TeamScoringEnabled,
            session.CurrentBuzzHolderPlayerId, buzzHolderPseudo, players, teams, themeBoard,
            session.CurrentAnswererPlayerId, answererPseudo, activeRandomDraw, activeStrawPoll,
            jokerGrants, session.AloneInTheWorldPlayerId, session.AloneInTheWorldTeamId,
            session.MeFirstHolderPlayerId, session.MeFirstHolderTeamId, session.MeFirstQuestionsRemaining,
            session.MeFirstConsumedThisQuestion, session.CurrentThemeSubRoundId);
    }

    private async Task<PlayerDto> BuildPlayerDto(int playerId, int sessionId, string? pseudo)
    {
        var player = await db.Players.SingleAsync(p => p.Id == playerId);
        pseudo ??= player.Pseudo;
        var score = await ComputeScore(playerId, sessionId);
        var teamScore = player.TeamId is null ? 0 : await ComputeTeamScore(player.TeamId.Value, sessionId);
        return new PlayerDto(playerId, pseudo, score, player.TeamId, teamScore, score + teamScore);
    }

    private async Task<TeamDto> BuildTeamDto(Team team, int sessionId)
    {
        var score = await ComputeTeamScore(team.Id, sessionId);
        var playerIds = await db.Players.Where(p => p.TeamId == team.Id).Select(p => p.Id).ToListAsync();
        return new TeamDto(team.Id, team.Name, playerIds, score);
    }

    /// <summary>Score perso : exclut les réponses tombées dans un pot d'équipe (Answer.TeamId non-null).</summary>
    private async Task<int> ComputeScore(int playerId, int sessionId)
    {
        var answerPoints = await db.Answers
            .Where(a => a.PlayerId == playerId && a.SessionId == sessionId && a.TeamId == null)
            .SumAsync(a => (int?)a.PointsAwarded) ?? 0;

        var adjustmentPoints = await db.ScoreAdjustments
            .Where(a => a.PlayerId == playerId && a.SessionId == sessionId)
            .SumAsync(a => (int?)a.Delta) ?? 0;

        return answerPoints + adjustmentPoints;
    }

    private async Task<int> ComputeTeamScore(int teamId, int sessionId)
    {
        var answerPoints = await db.Answers
            .Where(a => a.TeamId == teamId && a.SessionId == sessionId)
            .SumAsync(a => (int?)a.PointsAwarded) ?? 0;

        var adjustmentPoints = await db.ScoreAdjustments
            .Where(a => a.TeamId == teamId && a.SessionId == sessionId)
            .SumAsync(a => (int?)a.Delta) ?? 0;

        return answerPoints + adjustmentPoints;
    }

    private async Task<Dictionary<int, int>> ComputeAllScores(int sessionId)
    {
        var answerScores = await db.Answers
            .Where(a => a.SessionId == sessionId && a.TeamId == null)
            .GroupBy(a => a.PlayerId)
            .Select(g => new { PlayerId = g.Key, Total = g.Sum(a => a.PointsAwarded) })
            .ToDictionaryAsync(x => x.PlayerId, x => x.Total);

        var adjustmentScores = await db.ScoreAdjustments
            .Where(a => a.SessionId == sessionId && a.PlayerId != null)
            .GroupBy(a => a.PlayerId!.Value)
            .Select(g => new { PlayerId = g.Key, Total = g.Sum(a => a.Delta) })
            .ToDictionaryAsync(x => x.PlayerId, x => x.Total);

        var result = new Dictionary<int, int>(answerScores);
        foreach (var (playerId, total) in adjustmentScores)
        {
            result[playerId] = result.GetValueOrDefault(playerId) + total;
        }

        return result;
    }

    private async Task<Dictionary<int, int>> ComputeAllTeamScores(int sessionId)
    {
        var answerScores = await db.Answers
            .Where(a => a.SessionId == sessionId && a.TeamId != null)
            .GroupBy(a => a.TeamId!.Value)
            .Select(g => new { TeamId = g.Key, Total = g.Sum(a => a.PointsAwarded) })
            .ToDictionaryAsync(x => x.TeamId, x => x.Total);

        var adjustmentScores = await db.ScoreAdjustments
            .Where(a => a.SessionId == sessionId && a.TeamId != null)
            .GroupBy(a => a.TeamId!.Value)
            .Select(g => new { TeamId = g.Key, Total = g.Sum(a => a.Delta) })
            .ToDictionaryAsync(x => x.TeamId, x => x.Total);

        var result = new Dictionary<int, int>(answerScores);
        foreach (var (teamId, total) in adjustmentScores)
        {
            result[teamId] = result.GetValueOrDefault(teamId) + total;
        }

        return result;
    }
}
