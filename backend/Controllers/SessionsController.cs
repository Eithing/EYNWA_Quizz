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
public class SessionsController(QuizPartyDbContext db, FeatureEngineRegistry engineRegistry, IHubContext<GameHub> hub) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // ------------------------------------------------------------------
    // Game Master
    // ------------------------------------------------------------------

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

    /// <summary>Remplace l'inventaire complet des jokers de la session — appelé depuis le lobby, peut être
    /// rappelé pour ajuster tant que la partie n'a pas démarré (aucune contrainte de statut : rien
    /// n'empêche de le faire aussi en cours de partie si besoin).</summary>
    [Authorize]
    [HttpPost("{id:int}/jokers/grants")]
    public async Task<ActionResult<GameSessionStateDto>> SetJokerGrants(int id, SetJokerGrantsRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;

        var validPlayerIds = session.Players.Select(p => p.Id).ToHashSet();
        var validTeamIds = session.Teams.Select(t => t.Id).ToHashSet();

        foreach (var grant in request.Grants)
        {
            if (!Enum.TryParse<JokerType>(grant.Type, out _))
            {
                return BadRequest($"Type de joker invalide : {grant.Type}.");
            }

            if (grant.PlayerId is null == grant.TeamId is null)
            {
                return BadRequest("Chaque attribution doit cibler soit un joueur, soit une équipe (pas les deux, pas aucun).");
            }

            if (grant.PlayerId is not null && !validPlayerIds.Contains(grant.PlayerId.Value))
            {
                return BadRequest("Joueur introuvable dans cette session.");
            }

            if (grant.TeamId is not null && !validTeamIds.Contains(grant.TeamId.Value))
            {
                return BadRequest("Équipe introuvable dans cette session.");
            }

            if (grant.Charges < 0)
            {
                return BadRequest("Le nombre de charges ne peut pas être négatif.");
            }
        }

        var existing = await db.JokerGrants.Where(g => g.SessionId == id).ToListAsync();
        db.JokerGrants.RemoveRange(existing);

        foreach (var grant in request.Grants.Where(g => g.Charges > 0))
        {
            db.JokerGrants.Add(new JokerGrant
            {
                SessionId = id,
                Type = Enum.Parse<JokerType>(grant.Type),
                PlayerId = grant.PlayerId,
                TeamId = grant.TeamId,
                Charges = grant.Charges
            });
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

    /// <summary>Le GM choisit un thème du plateau et désigne dans la foulée qui y participe (joueurs ou
    /// équipes) — une seule action, pas de détour par un état d'attente séparé.</summary>
    [Authorize]
    [HttpPost("{id:int}/themes/{subRoundId:int}/choose")]
    public async Task<ActionResult<GameSessionStateDto>> ChooseTheme(int id, int subRoundId, ChooseThemeRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        if (session.Status != GameSessionStatus.ChoosingTheme)
        {
            return BadRequest("La session n'est pas sur un plateau de thèmes.");
        }

        var topRound = TopLevelRounds(quiz).ElementAtOrDefault(session.CurrentRoundIndex);
        var subRound = topRound?.SubRounds.SingleOrDefault(sr => sr.Id == subRoundId);
        if (subRound is null)
        {
            return BadRequest("Thème introuvable dans cette manche.");
        }

        var themeState = await db.ThemeStates.SingleOrDefaultAsync(t => t.SessionId == id && t.SubRoundId == subRoundId);
        if (themeState is null || themeState.Resolution != ThemeResolution.Pending)
        {
            return BadRequest("Ce thème a déjà été joué ou skippé.");
        }

        var validationError = await ApplyRoundParticipantsAsync(session, request.PlayerIds, request.TeamIds);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        themeState.IsRevealed = true;
        session.CurrentThemeSubRoundId = subRoundId;
        session.CurrentQuestionIndex = 0;
        // Ne démarre pas encore le minuteur : le thème est désigné mais reste en attente de lancement
        // explicite (voir /themes/{subRoundId}/launch) — fenêtre pendant laquelle le joker Échange peut
        // voler la désignation avant que la manche ne commence pour de bon.
        session.Status = GameSessionStatus.ThemeReadyToLaunch;

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    /// <summary>Démarre pour de bon un thème désigné via ChooseTheme (statut ThemeReadyToLaunch) — le
    /// minuteur ne se lance qu'ici, jamais dans ChooseTheme, pour laisser la fenêtre de vol du joker
    /// Échange se refermer explicitement à l'initiative du GM.</summary>
    [Authorize]
    [HttpPost("{id:int}/themes/{subRoundId:int}/launch")]
    public async Task<ActionResult<GameSessionStateDto>> LaunchTheme(int id, int subRoundId)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        if (session.Status != GameSessionStatus.ThemeReadyToLaunch || session.CurrentThemeSubRoundId != subRoundId)
        {
            return BadRequest("Ce thème n'est pas en attente de lancement.");
        }

        session.Status = GameSessionStatus.Running;
        session.CurrentQuestionStartedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [Authorize]
    [HttpPost("{id:int}/themes/{subRoundId:int}/skip")]
    public async Task<ActionResult<GameSessionStateDto>> SkipTheme(int id, int subRoundId)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        if (session.Status != GameSessionStatus.ChoosingTheme)
        {
            return BadRequest("La session n'est pas sur un plateau de thèmes.");
        }

        var themeState = await db.ThemeStates.SingleOrDefaultAsync(t => t.SessionId == id && t.SubRoundId == subRoundId);
        if (themeState is null || themeState.Resolution != ThemeResolution.Pending)
        {
            return BadRequest("Ce thème a déjà été joué ou skippé.");
        }

        themeState.Resolution = ThemeResolution.Skipped;
        themeState.IsRevealed = true;

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    /// <summary>Révèle un thème du plateau aux joueurs (ou tous d'un coup si SubRoundId est null) — le
    /// plateau est caché par défaut, le GM choisit quand et quoi montrer.</summary>
    [Authorize]
    [HttpPost("{id:int}/themes/reveal")]
    public async Task<ActionResult<GameSessionStateDto>> RevealThemes(int id, RevealThemeRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        var topRound = TopLevelRounds(quiz).ElementAtOrDefault(session.CurrentRoundIndex);
        if (topRound?.IsThemePicker != true)
        {
            return BadRequest("La manche courante n'est pas une manche à thèmes.");
        }

        var subRoundIds = topRound.SubRounds.Select(sr => sr.Id).ToList();
        var query = db.ThemeStates.Where(t => t.SessionId == id && subRoundIds.Contains(t.SubRoundId));
        if (request.SubRoundId is not null)
        {
            query = query.Where(t => t.SubRoundId == request.SubRoundId);
        }

        var states = await query.ToListAsync();
        foreach (var state in states)
        {
            state.IsRevealed = true;
        }

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    /// <summary>Déclenche manuellement la résolution d'une feature à résolution différée (closest-guess en
    /// mode Manual) — le GM révèle le classement quand il le souhaite plutôt que d'attendre la fermeture
    /// automatique de la fenêtre.</summary>
    [Authorize]
    [HttpPost("{id:int}/reveal-deferred-scoring")]
    public async Task<ActionResult<GameSessionStateDto>> RevealDeferredScoring(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        var (round, question) = GetCurrentRoundAndQuestion(quiz, session);
        if (round is null || question is null)
        {
            return BadRequest("Aucune question en cours.");
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);
        if (!engine.DefersScoringUntilWindowClose(round.ConfigJson))
        {
            return BadRequest("Cette manche ne nécessite pas de révélation manuelle.");
        }

        var pending = await db.Answers.Where(a => a.SessionId == session.Id && a.QuestionId == question.Id && a.IsCorrect == null).ToListAsync();
        if (pending.Count == 0)
        {
            return BadRequest("Aucune estimation en attente.");
        }

        await ResolveDeferredScoringAsync(session, round, question, engine, pending);
        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    /// <summary>Désigne le joueur qui répond en privé à la question courante d'une manche "à quoi pense
    /// l'autre" — démarre le minuteur, personne d'autre ne peut soumettre de réponse tant que le GM n'a
    /// pas lancé la phase de devinette (voir StartPartnerGuessGuessing).</summary>
    [Authorize]
    [HttpPost("{id:int}/partner-guess/set-answerer")]
    public async Task<ActionResult<GameSessionStateDto>> SetPartnerGuessAnswerer(int id, SetPartnerGuessAnswererRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        if (session.Status != GameSessionStatus.AwaitingAnswerer)
        {
            return BadRequest("La session n'attend pas la désignation d'un répondant.");
        }

        var player = session.Players.SingleOrDefault(p => p.Id == request.PlayerId);
        if (player is null)
        {
            return BadRequest("Joueur introuvable dans cette session.");
        }

        session.CurrentAnswererPlayerId = player.Id;
        session.Status = GameSessionStatus.Running;
        session.CurrentQuestionStartedAt = DateTime.UtcNow;

        var existing = await db.RoundParticipants.Where(rp => rp.SessionId == id).ToListAsync();
        db.RoundParticipants.RemoveRange(existing);
        db.RoundParticipants.Add(new RoundParticipant { SessionId = id, PlayerId = player.Id });

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    /// <summary>Passe de la phase "réponse privée" à la phase "devinette" pour la question courante d'une
    /// manche "à quoi pense l'autre" : désigne qui a le droit d'essayer de deviner (joueur ou équipe,
    /// jamais le répondant lui-même), relance le minuteur.</summary>
    [Authorize]
    [HttpPost("{id:int}/partner-guess/start-guessing")]
    public async Task<ActionResult<GameSessionStateDto>> StartPartnerGuessGuessing(int id, SetRoundParticipantsRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        var (round, _) = GetCurrentRoundAndQuestion(quiz, session);
        if (round is null || round.FeatureTypeKey != "partner-guess" || session.Status != GameSessionStatus.Running || session.CurrentAnswererPlayerId is null)
        {
            return BadRequest("Cette manche n'est pas en phase de réponse privée.");
        }

        if (request.PlayerIds.Contains(session.CurrentAnswererPlayerId.Value))
        {
            return BadRequest("Le répondant ne peut pas deviner sa propre réponse.");
        }

        var validationError = await ApplyRoundParticipantsAsync(session, request.PlayerIds, request.TeamIds);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        session.CurrentQuestionStartedAt = DateTime.UtcNow;
        session.PausedAt = null;
        session.CurrentBuzzHolderPlayerId = null; ResetPerQuestionJokerEffects(session);

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

    [Authorize]
    [HttpGet("{id:int}/current-question-full")]
    public async Task<ActionResult<CurrentQuestionAdminDto>> GetCurrentQuestionFull(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        await CheckAutoAdvance(quiz, session);

        var (round, question) = GetCurrentRoundAndQuestion(quiz, session);
        if (round is null || question is null || session.CurrentQuestionStartedAt is null)
        {
            return NotFound("Aucune question en cours.");
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);
        var state = engine.ComputeState(round.ConfigJson, session.CurrentQuestionStartedAt.Value, session.PausedAt, DateTime.UtcNow);
        var correctFinders = await GetCorrectFinderPseudos(session.Id, question.Id);

        var awaitingDeferredResolution = engine.DefersScoringUntilWindowClose(round.ConfigJson) &&
            await db.Answers.AnyAsync(a => a.SessionId == session.Id && a.QuestionId == question.Id && a.IsCorrect == null);

        // Vue GM : le payload "brut" inclut la réponse du répondant comme réponse acceptée (à quoi pense
        // l'autre), pour référence pendant la phase de devinette.
        var adminPayloadJson = await ResolveEffectivePayloadJsonAsync(session, round, question);

        List<OrderListGroupStateDto>? orderListGroups = null;
        if (round.FeatureTypeKey == "order-list")
        {
            var allAnswers = await db.Answers
                .Where(a => a.SessionId == session.Id && a.QuestionId == question.Id)
                .ToListAsync();

            orderListGroups = allAnswers
                .Select(a =>
                {
                    var label = a.TeamId is not null
                        ? session.Teams.FirstOrDefault(t => t.Id == a.TeamId)?.Name ?? "Équipe"
                        : session.Players.FirstOrDefault(p => p.Id == a.PlayerId)?.Pseudo ?? "Joueur";
                    var currentOrder = JsonSerializer.Deserialize<List<string>>(a.RawAnswer, JsonOptions) ?? [];
                    return new OrderListGroupStateDto(label, currentOrder, a.IsCorrect is not null, a.IsCorrect is not null ? a.PointsAwarded : null);
                })
                .ToList();
        }

        return Ok(new CurrentQuestionAdminDto(
            round.Id, round.Title, round.FeatureTypeKey,
            question.Id, adminPayloadJson, round.ConfigJson,
            state.CurrentLevel, state.CurrentPoints, state.SecondsRemainingInStep, state.SecondsRemainingTotal, state.IsAnswerWindowOpen,
            engine.IsBuzzerMode(round.ConfigJson), correctFinders, state.SecondsElapsedTotal, session.PausedAt is not null,
            awaitingDeferredResolution, orderListGroups));
    }

    [Authorize]
    [HttpGet("{id:int}/current-question-answers")]
    public async Task<ActionResult<List<AnswerFeedDto>>> GetCurrentQuestionAnswers(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        var (_, question) = GetCurrentRoundAndQuestion(quiz, session);
        if (question is null)
        {
            return Ok(new List<AnswerFeedDto>());
        }

        var answers = await db.Answers
            .Include(a => a.Player)
            .Where(a => a.SessionId == id && a.QuestionId == question.Id)
            .OrderBy(a => a.SubmittedAt)
            .ToListAsync();

        return Ok(answers
            .Select(a => new AnswerFeedDto(a.Id, a.PlayerId, a.Player!.Pseudo, a.RawAnswer, a.IsCorrect, a.PointsAwarded, a.PendingPoints, a.SubmittedAt))
            .ToList());
    }

    /// <summary>
    /// Juge une réponse (première validation en mode Manuel) ou corrige un verdict déjà posé — y compris une
    /// réponse évaluée automatiquement, si le GM estime que la tolérance aux fautes s'est trompée, même après
    /// la fermeture de la fenêtre de réponse.
    /// </summary>
    [Authorize]
    [HttpPost("{id:int}/answers/{answerId:int}/validate")]
    public async Task<ActionResult<PlayerDto>> ValidateAnswer(int id, int answerId, ValidateAnswerRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var answer = await db.Answers
            .Include(a => a.Question).ThenInclude(q => q!.Round)
            .SingleOrDefaultAsync(a => a.Id == answerId && a.SessionId == id);
        if (answer is null)
        {
            return NotFound();
        }

        var wasPending = answer.IsCorrect is null;

        // En scoring au rang, le rang n'est connu qu'au moment de la validation (l'ordre de validation
        // par le GM peut différer de l'ordre d'envoi) : recalculé ici plutôt que de figer PendingPoints.
        var points = answer.PendingPoints;
        if (request.IsCorrect && answer.Question?.Round is { } round)
        {
            var engine = engineRegistry.Get(round.FeatureTypeKey);
            if (engine.UsesRankBasedScoring(round.ConfigJson))
            {
                var rank = await db.Answers.CountAsync(a => a.SessionId == id && a.QuestionId == answer.QuestionId && a.IsCorrect == true && a.Id != answer.Id);
                points = engine.PointsForRank(round.ConfigJson, rank);
            }
        }

        answer.IsCorrect = request.IsCorrect;
        answer.PointsAwarded = request.IsCorrect ? points : 0;
        answer.ValidatedByGmAt = DateTime.UtcNow;

        var (session, _) = loaded.Value;
        if (wasPending)
        {
            // Ne reprend le minuteur que si c'était la dernière réponse en attente de jugement pour cette question.
            await ResumeAfterReviewIfClearAsync(session, answer.QuestionId, excludingAnswerId: answer.Id);
        }

        await db.SaveChangesAsync();

        var playerDto = await BuildPlayerDto(answer.PlayerId, session.Id, answer.Player?.Pseudo);
        await hub.Clients.Group(session.InviteToken).SendAsync("ScoreUpdated", playerDto);

        return Ok(playerDto);
    }

    [Authorize]
    [HttpPost("{id:int}/buzzer/resolve")]
    public async Task<ActionResult<GameSessionStateDto>> ResolveBuzz(int id, ResolveBuzzRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        if (session.CurrentBuzzHolderPlayerId is null)
        {
            return BadRequest("Personne n'a la main actuellement.");
        }

        var (round, question) = GetCurrentRoundAndQuestion(quiz, session);
        if (round is null || question is null)
        {
            return BadRequest("Aucune question en cours.");
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);
        var holderId = session.CurrentBuzzHolderPlayerId.Value;
        var points = await ComputePointsIfCorrect(engine, round.ConfigJson, session.Id, question.Id, 0);
        var holder = session.Players.Single(p => p.Id == holderId);

        var buzzSubmittedAt = DateTime.UtcNow;
        var buzzPointsAwarded = request.IsCorrect ? points : 0;

        db.Answers.Add(new Answer
        {
            SessionId = session.Id,
            PlayerId = holderId,
            QuestionId = question.Id,
            RawAnswer = "(buzzer)",
            IsCorrect = request.IsCorrect,
            PendingPoints = points,
            PointsAwarded = buzzPointsAwarded,
            TeamId = session.TeamScoringEnabled ? holder.TeamId : null,
            ValidationMode = AnswerValidationMode.Manual,
            SubmittedAt = buzzSubmittedAt,
            ValidatedByGmAt = buzzSubmittedAt
        });

        if (request.IsCorrect)
        {
            // "À quoi pense l'autre" : la bonne réponse devinée vient du répondant (phase 1) — il gagne
            // les mêmes points que le devineur, pas seulement ce dernier.
            await AwardPartnerGuessAnswererBonusAsync(session, round, question, buzzPointsAwarded, buzzSubmittedAt);
        }

        // Ne PAS appeler ResetPerQuestionJokerEffects ici : la question n'est pas terminée (juste le
        // verdict du buzz en cours), un retry classique peut suivre si la réponse est fausse — décrémenter
        // Moi d'abord ou effacer Seul au monde à ce stade serait prématuré (voir les autres call-sites de
        // CurrentBuzzHolderPlayerId = null, qui correspondent eux à de vraies transitions de question).
        session.CurrentBuzzHolderPlayerId = null;
        await ResumeAfterReviewIfClearAsync(session, question.Id);
        await db.SaveChangesAsync();

        if (request.IsCorrect)
        {
            var playerDto = await BuildPlayerDto(holderId, session.Id, null);
            await hub.Clients.Group(session.InviteToken).SendAsync("ScoreUpdated", playerDto);

            if (round.FeatureTypeKey == "partner-guess" && session.CurrentAnswererPlayerId is not null)
            {
                var answererDto = await BuildPlayerDto(session.CurrentAnswererPlayerId.Value, session.Id, null);
                await hub.Clients.Group(session.InviteToken).SendAsync("ScoreUpdated", answererDto);
            }
        }

        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [Authorize]
    [HttpPost("{id:int}/score-adjustments")]
    public async Task<ActionResult<PlayerDto>> AdjustScore(int id, ScoreAdjustmentRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, _) = loaded.Value;

        if (request.PlayerId is null)
        {
            return BadRequest("PlayerId requis pour un ajustement de score perso.");
        }

        var player = await db.Players.SingleOrDefaultAsync(p => p.Id == request.PlayerId && p.SessionId == id);
        if (player is null)
        {
            return NotFound("Joueur introuvable dans cette session.");
        }

        db.ScoreAdjustments.Add(new ScoreAdjustment
        {
            SessionId = id,
            PlayerId = request.PlayerId,
            QuestionId = request.QuestionId,
            Delta = request.Delta,
            Reason = request.Reason,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var playerDto = await BuildPlayerDto(player.Id, id, player.Pseudo);
        await hub.Clients.Group(session.InviteToken).SendAsync("ScoreUpdated", playerDto);

        return Ok(playerDto);
    }

    [Authorize]
    [HttpPost("{id:int}/teams/{teamId:int}/score-adjustments")]
    public async Task<ActionResult<TeamDto>> AdjustTeamScore(int id, int teamId, TeamScoreAdjustmentRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, _) = loaded.Value;

        var team = await db.Teams.SingleOrDefaultAsync(t => t.Id == teamId && t.SessionId == id);
        if (team is null)
        {
            return NotFound("Équipe introuvable dans cette session.");
        }

        db.ScoreAdjustments.Add(new ScoreAdjustment
        {
            SessionId = id,
            TeamId = teamId,
            Delta = request.Delta,
            Reason = request.Reason,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var teamDto = await BuildTeamDto(team, id);
        await hub.Clients.Group(session.InviteToken).SendAsync("TeamScoreUpdated", teamDto);

        return Ok(teamDto);
    }

    // ------------------------------------------------------------------
    // Outils host (tirage aléatoire, sondage) — indépendants du statut de la session, un seul actif à la fois
    // ------------------------------------------------------------------

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

    // ------------------------------------------------------------------
    // Joueurs (anonyme)
    // ------------------------------------------------------------------

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

    [AllowAnonymous]
    [HttpGet("by-token/{token}/current-question")]
    public async Task<ActionResult<PlayerQuestionDto>> GetCurrentQuestionForPlayer(string token, [FromQuery] Guid connectionToken)
    {
        var loaded = await LoadSessionByToken(token);
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        await CheckAutoAdvance(quiz, session);

        var (round, question) = GetCurrentRoundAndQuestion(quiz, session);
        if (round is null || question is null || session.CurrentQuestionStartedAt is null)
        {
            return NotFound("Aucune question en cours.");
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);
        var state = engine.ComputeState(round.ConfigJson, session.CurrentQuestionStartedAt.Value, session.PausedAt, DateTime.UtcNow);
        var effectivePayloadForPlayer = await ResolveEffectivePayloadJsonAsync(session, round, question);
        var publicPayloadJson = engine.BuildPublicPayloadJson(effectivePayloadForPlayer, round.ConfigJson);

        var player = await db.Players.SingleOrDefaultAsync(p => p.SessionId == session.Id && p.ConnectionToken == connectionToken);

        // Joker Cinquante-cinquante : effet personnel, jamais partagé — retire les options masquées POUR
        // CE joueur du payload public juste avant de le renvoyer, sans toucher à QcmEngine.
        if (round.FeatureTypeKey == "multiple-choice" && player is not null)
        {
            var reveal = await db.QcmFiftyFiftyReveals.SingleOrDefaultAsync(r => r.QuestionId == question.Id && r.PlayerId == player.Id);
            if (reveal is not null)
            {
                var hiddenIds = JsonSerializer.Deserialize<List<string>>(reveal.HiddenOptionIdsJson, JsonOptions) ?? [];
                publicPayloadJson = QcmEngine.RemoveOptions(publicPayloadJson, hiddenIds);
            }
        }

        var lastAnswer = player is null ? null : await GetLastAnswer(player.Id, question.Id);
        // Bloqué si aucune tentative n'est en cours (correcte ou en attente de validation manuelle),
        // ou si la dernière tentative était fausse mais qu'aucun nouvel essai n'est encore permis.
        var hasAnswered = lastAnswer is not null && !await CanPlayerRetryAsync(session, question, engine, round.ConfigJson, lastAnswer);
        var correctFinders = await GetCorrectFinderPseudos(session.Id, question.Id);
        var eligiblePlayerIds = await GetEligiblePlayerIdsAsync(session, round);
        var isSpectator = player is null || !eligiblePlayerIds.Contains(player.Id);

        List<ClosestGuessEntryDto>? closestGuessEntries = null;
        double? closestGuessTargetValue = null;
        if (round.FeatureTypeKey == "closest-guess" && !state.IsAnswerWindowOpen)
        {
            var allAnswers = await db.Answers
                .Include(a => a.Player)
                .Where(a => a.SessionId == session.Id && a.QuestionId == question.Id)
                .OrderBy(a => a.SubmittedAt)
                .ToListAsync();

            closestGuessEntries = allAnswers
                .Select(a => new ClosestGuessEntryDto(a.Player!.Pseudo, a.RawAnswer, a.IsCorrect, a.IsCorrect is null ? null : a.PointsAwarded))
                .ToList();

            if (allAnswers.Any(a => a.IsCorrect is not null))
            {
                closestGuessTargetValue = engine.GetNumericTarget(question.PayloadJson);
            }
        }

        List<string>? orderListCurrentOrder = null;
        List<string>? orderListCorrectOrder = null;
        List<string>? orderListChainItemIds = null;
        int? orderListPointsAwarded = null;
        if (round.FeatureTypeKey == "order-list" && player is not null && !isSpectator)
        {
            var groupAnswer = await GetOrderListGroupAnswerAsync(session, question.Id, player);

            // Personne du groupe n'a encore de brouillon pour cette question : on en crée un tout de
            // suite (ordre mélangé côté serveur) plutôt que de laisser chaque client calculer son propre
            // ordre initial — sans ça, deux coéquipiers qui arrivent en même temps sur la question
            // verraient chacun un ordre différent tant que personne n'a encore bougé un item.
            if (groupAnswer is null && state.IsAnswerWindowOpen)
            {
                var payload = JsonSerializer.Deserialize<OrderListQuestionPayload>(question.PayloadJson, JsonOptions) ?? new OrderListQuestionPayload();
                var shuffledIds = payload.Items.Select(it => it.Id).ToList();
                var random = Random.Shared;
                for (var i = shuffledIds.Count - 1; i > 0; i--)
                {
                    var j = random.Next(i + 1);
                    (shuffledIds[i], shuffledIds[j]) = (shuffledIds[j], shuffledIds[i]);
                }

                groupAnswer = new Answer
                {
                    SessionId = session.Id,
                    PlayerId = player.Id,
                    QuestionId = question.Id,
                    RawAnswer = JsonSerializer.Serialize(shuffledIds),
                    IsCorrect = null,
                    PendingPoints = 0,
                    PointsAwarded = 0,
                    TeamId = session.TeamScoringEnabled ? player.TeamId : null,
                    ValidationMode = AnswerValidationMode.Auto,
                    SubmittedAt = DateTime.UtcNow
                };
                db.Answers.Add(groupAnswer);
                await db.SaveChangesAsync();
            }

            if (groupAnswer is not null)
            {
                orderListCurrentOrder = JsonSerializer.Deserialize<List<string>>(groupAnswer.RawAnswer, JsonOptions);

                if (groupAnswer.IsCorrect is not null)
                {
                    var payload = JsonSerializer.Deserialize<OrderListQuestionPayload>(question.PayloadJson, JsonOptions) ?? new OrderListQuestionPayload();
                    orderListCorrectOrder = payload.Items.Select(it => it.Id).ToList();
                    orderListChainItemIds = OrderListEngine.ComputeChainItemIds(question.PayloadJson, groupAnswer.RawAnswer);
                    orderListPointsAwarded = groupAnswer.PointsAwarded;
                }
            }
        }

        return Ok(new PlayerQuestionDto(
            question.Id, round.Title, round.FeatureTypeKey, publicPayloadJson,
            state.CurrentLevel, state.SecondsRemainingInStep, state.SecondsRemainingTotal, state.IsAnswerWindowOpen, hasAnswered, correctFinders, isSpectator,
            engine.IsBuzzerMode(round.ConfigJson), state.SecondsElapsedTotal, session.PausedAt is not null,
            lastAnswer?.IsCorrect, lastAnswer?.IsCorrect is not null ? lastAnswer.PointsAwarded : null,
            closestGuessEntries, closestGuessTargetValue,
            orderListCurrentOrder, orderListCorrectOrder, orderListChainItemIds, orderListPointsAwarded));
    }

    [AllowAnonymous]
    [HttpPost("by-token/{token}/buzz")]
    public async Task<ActionResult<GameSessionStateDto>> Buzz(string token, BuzzRequest request)
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

        var (round, question) = GetCurrentRoundAndQuestion(quiz, session);
        if (round is null || question is null || session.CurrentQuestionStartedAt is null)
        {
            return BadRequest("Aucune question en cours.");
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);
        if (!engine.IsBuzzerMode(round.ConfigJson))
        {
            return BadRequest("Cette question n'est pas une question de rapidité.");
        }

        var buzzEligibleIds = await GetEligiblePlayerIdsAsync(session, round);
        if (!buzzEligibleIds.Contains(player.Id))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Vous êtes spectateur pour cette manche.");
        }

        var state = engine.ComputeState(round.ConfigJson, session.CurrentQuestionStartedAt.Value, session.PausedAt, DateTime.UtcNow);
        if (!state.IsAnswerWindowOpen)
        {
            return BadRequest("Le temps de réponse est écoulé.");
        }

        if (session.CurrentBuzzHolderPlayerId is not null)
        {
            return Conflict("Un autre joueur a déjà la main.");
        }

        // Joker Moi d'abord : verrouille le buzzer pour tout le monde sauf le détenteur tant qu'il n'a pas
        // buzzé sur cette question — une fois qu'il l'a fait (MeFirstConsumedThisQuestion), le retry
        // classique reprend normalement pour tous en cas de mauvaise réponse (voir ResolveBuzz).
        if (session.MeFirstQuestionsRemaining > 0 && !session.MeFirstConsumedThisQuestion)
        {
            var isMeFirstHolder = session.MeFirstHolderPlayerId == player.Id
                || (player.TeamId is not null && session.MeFirstHolderTeamId == player.TeamId);
            if (!isMeFirstHolder)
            {
                return Conflict("Un autre joueur a la priorité pour buzzer sur cette question (joker Moi d'abord).");
            }
        }

        var lastAnswer = await GetLastAnswer(player.Id, question.Id);
        if (lastAnswer is not null && !await CanPlayerRetryAsync(session, question, engine, round.ConfigJson, lastAnswer))
        {
            return Conflict("Vous avez déjà utilisé votre tentative sur cette question.");
        }

        session.CurrentBuzzHolderPlayerId = player.Id;
        if (session.MeFirstQuestionsRemaining > 0)
        {
            session.MeFirstConsumedThisQuestion = true;
        }
        // Le temps s'arrête tant que le GM n'a pas jugé la réponse orale du joueur qui a la main.
        PauseForPendingReview(session);
        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    /// <summary>Point d'entrée unique pour l'utilisation d'un joker — dispatche vers la logique propre à
    /// chaque type (voir Use*Async ci-dessous), décrémente la charge et diffuse un toast "JokerUsed" en
    /// cas de succès. Pas d'interface pluggable façon IFeatureEngine : 5 jokers fixes, un switch suffit.</summary>
    [AllowAnonymous]
    [HttpPost("by-token/{token}/jokers/use")]
    public async Task<ActionResult<GameSessionStateDto>> UseJoker(string token, UseJokerRequest request)
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

        if (!Enum.TryParse<JokerType>(request.Type, out var type))
        {
            return BadRequest("Type de joker invalide.");
        }

        var grant = await FindUsableJokerGrantAsync(session, player, type);
        if (grant is null)
        {
            return BadRequest("Aucune charge disponible pour ce joker.");
        }

        // Décrémente la charge de façon atomique AVANT d'appliquer l'effet (UPDATE conditionné sur
        // Charges > 0, en dehors du change tracker) : sans ça, deux utilisations concurrentes de la
        // même charge (ex: un joker d'équipe utilisé par deux coéquipiers en même temps) peuvent
        // toutes les deux passer la vérification ci-dessus et déclencher l'effet deux fois pour une
        // seule charge décomptée. Si la ligne a déjà été consommée entre-temps, on s'arrête ici, avant
        // tout effet.
        var decremented = await db.JokerGrants
            .Where(g => g.Id == grant.Id && g.Charges > 0)
            .ExecuteUpdateAsync(setters => setters.SetProperty(g => g.Charges, g => g.Charges - 1));
        if (decremented == 0)
        {
            return BadRequest("Aucune charge disponible pour ce joker.");
        }

        var (round, question) = GetCurrentRoundAndQuestion(quiz, session);

        var result = type switch
        {
            JokerType.FiftyFifty => await UseFiftyFiftyAsync(session, round, question, player),
            JokerType.MeFirst => await UseMeFirstAsync(session, round, grant, player),
            JokerType.AloneInTheWorld => await UseAloneInTheWorldAsync(session, round, question, grant, player),
            JokerType.CopyPaste => await UseCopyPasteAsync(session, round, question, player, request.TargetPlayerId),
            JokerType.Exchange => await UseExchangeAsync(session, quiz, grant, player),
            _ => (Success: false, Error: "Ce joker n'est pas encore disponible.", Detail: (string?)null, TargetPlayer: (Player?)null)
        };

        if (!result.Success)
        {
            // L'effet a été refusé après coup (ex: fenêtre fermée entre la vérification et l'exécution) :
            // on rend la charge décomptée ci-dessus plutôt que de la perdre pour rien.
            await db.JokerGrants
                .Where(g => g.Id == grant.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(g => g.Charges, g => g.Charges + 1));
            return BadRequest(result.Error);
        }

        db.JokerUsageEvents.Add(new JokerUsageEvent
        {
            SessionId = session.Id,
            Type = type,
            ActorPlayerId = grant.PlayerId,
            ActorTeamId = grant.TeamId,
            TargetPlayerId = result.TargetPlayer?.Id,
            Detail = result.Detail,
            UsedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var actorLabel = grant.TeamId is not null
            ? session.Teams.SingleOrDefault(t => t.Id == grant.TeamId)?.Name ?? "Équipe"
            : player.Pseudo;
        var jokerUsedEvent = new JokerUsedEventDto(type.ToString(), actorLabel, result.TargetPlayer?.Pseudo, result.Detail);
        await hub.Clients.Group(session.InviteToken).SendAsync("JokerUsed", jokerUsedEvent);
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [AllowAnonymous]
    [HttpPost("by-token/{token}/answer")]
    public async Task<ActionResult<SubmitAnswerResponse>> SubmitAnswer(string token, SubmitAnswerRequest request)
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

        var (round, question) = GetCurrentRoundAndQuestion(quiz, session);
        if (round is null || question is null || session.CurrentQuestionStartedAt is null)
        {
            return BadRequest("Aucune question en cours.");
        }

        var answerEligibleIds = await GetEligiblePlayerIdsAsync(session, round);
        if (!answerEligibleIds.Contains(player.Id))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Vous êtes spectateur pour cette manche.");
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);

        var lastAnswer = await GetLastAnswer(player.Id, question.Id);
        if (lastAnswer is not null && !await CanPlayerRetryAsync(session, question, engine, round.ConfigJson, lastAnswer))
        {
            return Conflict("Réponse déjà envoyée pour cette question.");
        }

        var submittedAt = DateTime.UtcNow;

        // "À quoi pense l'autre", phase 1 : le répondant écrit TOUJOURS sa réponse en privé (avant même le
        // check buzzer ci-dessous, qui ne concerne que la phase de devinette) — jamais notée, elle sert
        // uniquement de cible pour la phase de devinette qui suit (voir StartPartnerGuessGuessing).
        if (round.FeatureTypeKey == "partner-guess" && player.Id == session.CurrentAnswererPlayerId)
        {
            db.Answers.Add(new Answer
            {
                SessionId = session.Id,
                PlayerId = player.Id,
                QuestionId = question.Id,
                RawAnswer = request.RawAnswer,
                IsCorrect = null,
                PendingPoints = 0,
                PointsAwarded = 0,
                ValidationMode = AnswerValidationMode.Auto,
                SubmittedAt = submittedAt
            });
            await db.SaveChangesAsync();

            return Ok(new SubmitAnswerResponse(null, 0, "Auto"));
        }

        if (engine.IsBuzzerMode(round.ConfigJson))
        {
            return BadRequest("Cette question se joue au buzzer : utilisez le bouton dédié.");
        }

        var effectivePayloadJson = await ResolveEffectivePayloadJsonAsync(session, round, question);

        var elapsedSeconds = SessionTiming.ComputeElapsedSeconds(session.CurrentQuestionStartedAt.Value, session.PausedAt, submittedAt);
        // Calculé AVANT d'insérer cette réponse : en scoring au rang, le rang doit exclure la tentative en cours.
        var pendingPoints = await ComputePointsIfCorrect(engine, round.ConfigJson, session.Id, question.Id, elapsedSeconds);
        var evaluation = engine.Evaluate(round.ConfigJson, effectivePayloadJson, request.RawAnswer, session.CurrentQuestionStartedAt.Value, session.PausedAt, submittedAt);
        // Scoring au rang : le rang exact (donc les points) ne peut être connu qu'ici, au moment de la
        // soumission (ComputePointsIfCorrect ci-dessus) — Evaluate() ne le sait pas. Sinon (fixe, dégressif
        // par palier de zoom, ou réponses multiples avec crédit partiel), evaluation.PointsAwarded est déjà
        // le bon montant final ; le reprendre tel quel évite d'écraser un crédit partiel par 0 juste parce
        // qu'IsCorrect (toutes les réponses trouvées) est faux.
        var pointsAwarded = engine.UsesRankBasedScoring(round.ConfigJson)
            ? (evaluation.IsCorrect == true ? pendingPoints : 0)
            : evaluation.PointsAwarded;

        // Joker Seul au monde : si actif sur cette question, seule la réponse du détenteur compte pour
        // les points — les autres restent jugées normalement (juste/faux visible pour le joueur) mais ne
        // rapportent rien.
        if (session.AloneInTheWorldPlayerId is not null || session.AloneInTheWorldTeamId is not null)
        {
            var isAloneInTheWorldHolder = session.AloneInTheWorldPlayerId == player.Id
                || (player.TeamId is not null && session.AloneInTheWorldTeamId == player.TeamId);
            if (!isAloneInTheWorldHolder)
            {
                pointsAwarded = 0;
            }
        }

        var answer = new Answer
        {
            SessionId = session.Id,
            PlayerId = player.Id,
            QuestionId = question.Id,
            RawAnswer = request.RawAnswer,
            IsCorrect = evaluation.IsCorrect,
            PendingPoints = pendingPoints,
            PointsAwarded = pointsAwarded,
            TeamId = session.TeamScoringEnabled ? player.TeamId : null,
            ValidationMode = engine.IsManualValidation(round.ConfigJson) ? AnswerValidationMode.Manual : AnswerValidationMode.Auto,
            SubmittedAt = submittedAt
        };

        db.Answers.Add(answer);

        if (evaluation.IsCorrect == true)
        {
            // "À quoi pense l'autre" : la bonne réponse devinée vient du répondant (phase 1) — il gagne
            // les mêmes points que le devineur, pas seulement ce dernier.
            await AwardPartnerGuessAnswererBonusAsync(session, round, question, pointsAwarded, submittedAt);
        }

        if (evaluation.IsCorrect is null && !engine.DefersScoringUntilWindowClose(round.ConfigJson))
        {
            // Le temps s'arrête tant que le GM n'a pas jugé cette réponse en attente de validation manuelle.
            // Sans effet pour une feature à résolution différée (closest-guess) : la fenêtre doit rester
            // ouverte pour laisser le temps aux autres joueurs de soumettre leur propre estimation.
            PauseForPendingReview(session);
        }

        await db.SaveChangesAsync();

        if (evaluation.IsCorrect is not null)
        {
            var playerDto = await BuildPlayerDto(player.Id, session.Id, player.Pseudo);
            await hub.Clients.Group(session.InviteToken).SendAsync("ScoreUpdated", playerDto);

            if (evaluation.IsCorrect == true && round.FeatureTypeKey == "partner-guess" && session.CurrentAnswererPlayerId is not null)
            {
                var answererDto = await BuildPlayerDto(session.CurrentAnswererPlayerId.Value, session.Id, null);
                await hub.Clients.Group(session.InviteToken).SendAsync("ScoreUpdated", answererDto);
            }
        }
        else
        {
            await hub.Clients.Group(session.InviteToken).SendAsync("AnswerPendingValidation");
        }

        return Ok(new SubmitAnswerResponse(evaluation.IsCorrect, pointsAwarded, answer.ValidationMode.ToString()));
    }

    /// <summary>order-list : met à jour le brouillon partagé du groupe (joueur seul, ou toute son équipe en
    /// mode équipe) après un glisser-déposer terminé — ne note rien, juste la synchronisation en quasi
    /// temps réel. La finalisation (score) se fait via SubmitOrderFinal ou automatiquement à la fermeture
    /// de la fenêtre (voir FinalizeIndependentPendingAnswersIfDueAsync).</summary>
    [AllowAnonymous]
    [HttpPost("by-token/{token}/order-draft")]
    public async Task<ActionResult<GameSessionStateDto>> SubmitOrderDraft(string token, OrderDraftRequest request)
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

        var (round, question) = GetCurrentRoundAndQuestion(quiz, session);
        if (round is null || question is null || session.CurrentQuestionStartedAt is null || round.FeatureTypeKey != "order-list")
        {
            return BadRequest("Aucune question 'ordonne la liste' en cours.");
        }

        var eligibleIds = await GetEligiblePlayerIdsAsync(session, round);
        if (!eligibleIds.Contains(player.Id))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Vous êtes spectateur pour cette manche.");
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);
        var state = engine.ComputeState(round.ConfigJson, session.CurrentQuestionStartedAt.Value, session.PausedAt, DateTime.UtcNow);
        if (!state.IsAnswerWindowOpen)
        {
            return BadRequest("Le temps de réponse est écoulé.");
        }

        var groupAnswer = await GetOrderListGroupAnswerAsync(session, question.Id, player);
        if (groupAnswer is null)
        {
            db.Answers.Add(new Answer
            {
                SessionId = session.Id,
                PlayerId = player.Id,
                QuestionId = question.Id,
                RawAnswer = JsonSerializer.Serialize(request.ItemOrder),
                IsCorrect = null,
                PendingPoints = 0,
                PointsAwarded = 0,
                TeamId = session.TeamScoringEnabled ? player.TeamId : null,
                ValidationMode = AnswerValidationMode.Auto,
                SubmittedAt = DateTime.UtcNow
            });
        }
        else if (groupAnswer.IsCorrect is null)
        {
            groupAnswer.RawAnswer = JsonSerializer.Serialize(request.ItemOrder);
            // Dernier joueur à avoir bougé un item : simple attribution d'affichage, sans incidence sur
            // le score (qui passe par TeamId en mode équipe, jamais par ce PlayerId-ci).
            groupAnswer.PlayerId = player.Id;
            groupAnswer.SubmittedAt = DateTime.UtcNow;
        }
        else
        {
            return Conflict("Ce classement a déjà été validé.");
        }

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    /// <summary>order-list : finalise (note) le brouillon en cours du groupe du joueur — clic explicite
    /// "Valider mon classement". Si le temps s'écoule avant que quiconque du groupe ne clique, le même
    /// résultat est obtenu automatiquement (voir FinalizeIndependentPendingAnswersIfDueAsync).</summary>
    [AllowAnonymous]
    [HttpPost("by-token/{token}/order-submit")]
    public async Task<ActionResult<OrderSubmitResponse>> SubmitOrderFinal(string token, OrderSubmitRequest request)
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

        var (round, question) = GetCurrentRoundAndQuestion(quiz, session);
        if (round is null || question is null || session.CurrentQuestionStartedAt is null || round.FeatureTypeKey != "order-list")
        {
            return BadRequest("Aucune question 'ordonne la liste' en cours.");
        }

        var eligibleIds = await GetEligiblePlayerIdsAsync(session, round);
        if (!eligibleIds.Contains(player.Id))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Vous êtes spectateur pour cette manche.");
        }

        var groupAnswer = await GetOrderListGroupAnswerAsync(session, question.Id, player);
        if (groupAnswer is null)
        {
            return BadRequest("Aucun classement en cours à valider.");
        }
        if (groupAnswer.IsCorrect is not null)
        {
            return Conflict("Ce classement a déjà été validé.");
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);
        var evaluation = engine.Evaluate(round.ConfigJson, question.PayloadJson, groupAnswer.RawAnswer, session.CurrentQuestionStartedAt.Value, session.PausedAt, DateTime.UtcNow);
        groupAnswer.IsCorrect = evaluation.IsCorrect;
        groupAnswer.PointsAwarded = evaluation.PointsAwarded;
        groupAnswer.ValidatedByGmAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        var playerDto = await BuildPlayerDto(player.Id, session.Id, player.Pseudo);
        await hub.Clients.Group(session.InviteToken).SendAsync("ScoreUpdated", playerDto);
        await BroadcastState(session, quiz);

        var chainItemIds = OrderListEngine.ComputeChainItemIds(question.PayloadJson, groupAnswer.RawAnswer);

        return Ok(new OrderSubmitResponse(groupAnswer.PointsAwarded, chainItemIds));
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

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

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

    private async Task<bool> AllPlayersAnsweredCorrectly(GameSession session, Round round, Question question)
    {
        var eligiblePlayerIds = await GetEligiblePlayerIdsAsync(session, round);
        if (eligiblePlayerIds.Count == 0)
        {
            return false;
        }

        // Mode buzzer : une course, pas un test collectif — une seule bonne réponse clôt la question
        // (mais toujours restreinte aux participants éligibles de cette manche).
        if (!engineRegistry.Get(round.FeatureTypeKey).RequiresAllPlayersToAnswer(round.ConfigJson))
        {
            return await db.Answers.AnyAsync(a => a.SessionId == session.Id && a.QuestionId == question.Id
                && a.IsCorrect == true && eligiblePlayerIds.Contains(a.PlayerId));
        }

        var correctCount = await db.Answers.CountAsync(a => a.SessionId == session.Id && a.QuestionId == question.Id
            && a.IsCorrect == true && eligiblePlayerIds.Contains(a.PlayerId));
        return correctCount >= eligiblePlayerIds.Count;
    }

    /// <summary>Pour une feature à résolution différée (closest-guess) : tout le monde a-t-il soumis une
    /// estimation, indépendamment de si elle est jugée (IsCorrect reste null tant que non résolu) ?</summary>
    private async Task<bool> AllEligiblePlayersSubmittedAsync(GameSession session, Round round, Question question)
    {
        var eligiblePlayerIds = await GetEligiblePlayerIdsAsync(session, round);
        if (eligiblePlayerIds.Count == 0)
        {
            return false;
        }

        var answeredPlayerIds = await db.Answers
            .Where(a => a.SessionId == session.Id && a.QuestionId == question.Id && eligiblePlayerIds.Contains(a.PlayerId))
            .Select(a => a.PlayerId)
            .Distinct()
            .ToListAsync();

        return eligiblePlayerIds.All(answeredPlayerIds.Contains);
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

    /// <summary>
    /// "À quoi pense l'autre" : compose à la volée un payload avec la réponse privée du répondant comme
    /// "réponse acceptée", pour que PartnerGuessEngine (qui hérite tel quel de QaEngine) évalue
    /// normalement la tentative du devineur. Payload inchangé pour toute autre feature — le contrôleur
    /// reste agnostique de la feature dans tous les autres cas.
    /// </summary>
    private async Task<string> ResolveEffectivePayloadJsonAsync(GameSession session, Round round, Question question)
    {
        if (round.FeatureTypeKey != "partner-guess")
        {
            return question.PayloadJson;
        }

        var answererAnswer = session.CurrentAnswererPlayerId is null
            ? null
            : await db.Answers
                .Where(a => a.PlayerId == session.CurrentAnswererPlayerId && a.QuestionId == question.Id)
                .OrderByDescending(a => a.SubmittedAt)
                .Select(a => a.RawAnswer)
                .FirstOrDefaultAsync();

        string questionText;
        try
        {
            questionText = JsonDocument.Parse(question.PayloadJson).RootElement.GetProperty("questionText").GetString() ?? "";
        }
        catch
        {
            questionText = "";
        }

        var acceptedAnswers = answererAnswer is null ? [] : new[] { answererAnswer };
        return JsonSerializer.Serialize(new { questionText, acceptedAnswers });
    }

    /// <summary>"À quoi pense l'autre" : chaque fois qu'un devineur marque des points (buzzer ou saisie
    /// auto-validée), le répondant de la phase 1 gagne le même montant — c'est sa réponse qui a rendu le
    /// point possible, pas seulement celle du devineur. Sans effet si points &lt;= 0 (rien à gagner).</summary>
    private async Task AwardPartnerGuessAnswererBonusAsync(GameSession session, Round round, Question question, int points, DateTime submittedAt)
    {
        if (round.FeatureTypeKey != "partner-guess" || session.CurrentAnswererPlayerId is null || points <= 0)
        {
            return;
        }

        var answerer = session.Players.SingleOrDefault(p => p.Id == session.CurrentAnswererPlayerId);
        if (answerer is null)
        {
            return;
        }

        db.Answers.Add(new Answer
        {
            SessionId = session.Id,
            PlayerId = answerer.Id,
            QuestionId = question.Id,
            RawAnswer = "(bonus répondant)",
            IsCorrect = true,
            PendingPoints = points,
            PointsAwarded = points,
            TeamId = session.TeamScoringEnabled ? answerer.TeamId : null,
            ValidationMode = AnswerValidationMode.Auto,
            SubmittedAt = submittedAt,
            ValidatedByGmAt = submittedAt
        });
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

    /// <summary>Referme la fenêtre d'effet des jokers "actifs sur la question courante" à chaque vraie
    /// transition de question (mêmes call-sites que la remise à null de CurrentBuzzHolderPlayerId, SAUF
    /// ResolveBuzz qui ne termine pas forcément la question — voir son commentaire dédié) : Seul au monde
    /// est toujours effacé (effet strictement limité à une question) ; Moi d'abord décrémente son compteur
    /// tant qu'actif et efface son détenteur une fois à 0.</summary>
    private static void ResetPerQuestionJokerEffects(GameSession session)
    {
        session.AloneInTheWorldPlayerId = null;
        session.AloneInTheWorldTeamId = null;

        if (session.MeFirstQuestionsRemaining > 0)
        {
            session.MeFirstQuestionsRemaining--;
            session.MeFirstConsumedThisQuestion = false;
            if (session.MeFirstQuestionsRemaining == 0)
            {
                session.MeFirstHolderPlayerId = null;
                session.MeFirstHolderTeamId = null;
            }
        }
    }

    /// <summary>Trouve une charge de joker utilisable par ce joueur : soit attribuée directement à lui,
    /// soit à son équipe (stock partagé, n'importe quel membre peut piocher dedans).</summary>
    private async Task<JokerGrant?> FindUsableJokerGrantAsync(GameSession session, Player player, JokerType type)
    {
        return await db.JokerGrants.SingleOrDefaultAsync(g =>
            g.SessionId == session.Id && g.Type == type && g.Charges > 0 &&
            (g.PlayerId == player.Id || (player.TeamId != null && g.TeamId == player.TeamId)));
    }

    /// <summary>Joker Cinquante-cinquante : masque, pour CE joueur uniquement, la moitié des mauvaises
    /// options d'une question QCM en cours — le nombre de bonnes réponses attendues ne change pas.</summary>
    private async Task<(bool Success, string? Error, string? Detail, Player? TargetPlayer)> UseFiftyFiftyAsync(GameSession session, Round? round, Question? question, Player player)
    {
        if (round is null || question is null || round.FeatureTypeKey != "multiple-choice" || session.CurrentQuestionStartedAt is null)
        {
            return (false, "Aucune question à choix multiple en cours.", null, null);
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);
        var state = engine.ComputeState(round.ConfigJson, session.CurrentQuestionStartedAt.Value, session.PausedAt, DateTime.UtcNow);
        if (!state.IsAnswerWindowOpen)
        {
            return (false, "Le temps de réponse est écoulé.", null, null);
        }

        var eligibleIds = await GetEligiblePlayerIdsAsync(session, round);
        if (!eligibleIds.Contains(player.Id))
        {
            return (false, "Vous êtes spectateur pour cette manche.", null, null);
        }

        var alreadyUsed = await db.QcmFiftyFiftyReveals.AnyAsync(r => r.QuestionId == question.Id && r.PlayerId == player.Id);
        if (alreadyUsed)
        {
            return (false, "Déjà utilisé sur cette question.", null, null);
        }

        var payload = JsonSerializer.Deserialize<QcmQuestionPayload>(question.PayloadJson, JsonOptions) ?? new QcmQuestionPayload();
        var wrongOptionIds = payload.Options.Where(o => !o.IsCorrect).Select(o => o.Id).ToList();
        if (wrongOptionIds.Count < 2)
        {
            return (false, "Pas assez de mauvaises réponses pour utiliser ce joker.", null, null);
        }

        var toHide = wrongOptionIds.OrderBy(_ => Random.Shared.Next()).Take(wrongOptionIds.Count / 2).ToList();

        db.QcmFiftyFiftyReveals.Add(new QcmFiftyFiftyReveal
        {
            SessionId = session.Id,
            QuestionId = question.Id,
            PlayerId = player.Id,
            HiddenOptionIdsJson = JsonSerializer.Serialize(toHide)
        });

        return (true, null, null, null);
    }

    /// <summary>Manches où une "bonne réponse immédiate" a un sens (réponse simultanée jugée dans la
    /// foulée) — exclut closest-guess/partner-guess (résolution différée ou réponse privée), voir le plan
    /// approuvé pour Seul au monde/Copier-coller.</summary>
    private static readonly HashSet<string> SimultaneousAnswerFeatures =
        ["qa-text", "zoom-image", "blind-test", "image-guess", "multiple-choice", "order-list"];

    /// <summary>Joker Seul au monde : force que seule la réponse du détenteur compte pour les points sur
    /// la question courante — les réponses déjà soumises par les autres sont immédiatement remises à 0,
    /// et toute soumission future passe par le même garde-fou (voir SubmitAnswer).</summary>
    private async Task<(bool Success, string? Error, string? Detail, Player? TargetPlayer)> UseAloneInTheWorldAsync(GameSession session, Round? round, Question? question, JokerGrant grant, Player player)
    {
        if (round is null || question is null || session.CurrentQuestionStartedAt is null || !SimultaneousAnswerFeatures.Contains(round.FeatureTypeKey))
        {
            return (false, "Ce joker n'est pas utilisable sur cette manche.", null, null);
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);
        var state = engine.ComputeState(round.ConfigJson, session.CurrentQuestionStartedAt.Value, session.PausedAt, DateTime.UtcNow);
        if (!state.IsAnswerWindowOpen)
        {
            return (false, "Le temps de réponse est écoulé.", null, null);
        }

        var eligibleIds = await GetEligiblePlayerIdsAsync(session, round);
        if (!eligibleIds.Contains(player.Id))
        {
            return (false, "Vous êtes spectateur pour cette manche.", null, null);
        }

        if (session.AloneInTheWorldPlayerId is not null || session.AloneInTheWorldTeamId is not null)
        {
            return (false, "Déjà utilisé sur cette question.", null, null);
        }

        session.AloneInTheWorldPlayerId = grant.PlayerId;
        session.AloneInTheWorldTeamId = grant.TeamId;

        var existingAnswers = await db.Answers.Where(a => a.SessionId == session.Id && a.QuestionId == question.Id).ToListAsync();
        foreach (var existing in existingAnswers)
        {
            var isHolder = existing.PlayerId == grant.PlayerId || (grant.TeamId is not null && existing.TeamId == grant.TeamId);
            if (!isHolder)
            {
                existing.PointsAwarded = 0;
            }
        }

        return (true, null, null, null);
    }

    /// <summary>Joker Échange : pendant la fenêtre "thème désigné mais pas encore lancé"
    /// (ThemeReadyToLaunch), permet à un joueur non-participant de remplacer la désignation actuelle par
    /// lui-même (ou son équipe, selon le propriétaire de la charge de joker).</summary>
    private async Task<(bool Success, string? Error, string? Detail, Player? TargetPlayer)> UseExchangeAsync(GameSession session, Quiz quiz, JokerGrant grant, Player player)
    {
        if (session.Status != GameSessionStatus.ThemeReadyToLaunch || session.CurrentThemeSubRoundId is null)
        {
            return (false, "Aucun thème en attente de lancement.", null, null);
        }

        var participants = await db.RoundParticipants.Where(rp => rp.SessionId == session.Id).ToListAsync();
        var isAlreadyParticipant = participants.Any(rp =>
            rp.PlayerId == player.Id || (player.TeamId is not null && rp.TeamId == player.TeamId));
        if (isAlreadyParticipant)
        {
            return (false, "Tu fais déjà partie des participants de ce thème.", null, null);
        }

        var topRound = TopLevelRounds(quiz).ElementAtOrDefault(session.CurrentRoundIndex);
        var subRound = topRound?.SubRounds.SingleOrDefault(sr => sr.Id == session.CurrentThemeSubRoundId);

        db.RoundParticipants.RemoveRange(participants);
        db.RoundParticipants.Add(grant.TeamId is not null
            ? new RoundParticipant { SessionId = session.Id, TeamId = grant.TeamId }
            : new RoundParticipant { SessionId = session.Id, PlayerId = grant.PlayerId });

        if (grant.TeamId is not null)
        {
            session.TeamScoringEnabled = true;
        }

        return (true, null, subRound?.Title, null);
    }

    /// <summary>Joker Copier/coller : crée une assignation résolue à la fermeture de la fenêtre de réponse
    /// (voir ResolveCopyPasteAssignmentsAsync) — le copieur ne voit jamais la réponse de la cible avant
    /// que celle-ci ne devienne la sienne.</summary>
    private async Task<(bool Success, string? Error, string? Detail, Player? TargetPlayer)> UseCopyPasteAsync(GameSession session, Round? round, Question? question, Player player, int? targetPlayerId)
    {
        if (round is null || question is null || session.CurrentQuestionStartedAt is null || !SimultaneousAnswerFeatures.Contains(round.FeatureTypeKey))
        {
            return (false, "Ce joker n'est pas utilisable sur cette manche.", null, null);
        }

        if (targetPlayerId is null)
        {
            return (false, "Choisis un joueur à copier.", null, null);
        }

        if (targetPlayerId == player.Id)
        {
            return (false, "Tu ne peux pas te copier toi-même.", null, null);
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);
        var state = engine.ComputeState(round.ConfigJson, session.CurrentQuestionStartedAt.Value, session.PausedAt, DateTime.UtcNow);
        if (!state.IsAnswerWindowOpen)
        {
            return (false, "Le temps de réponse est écoulé.", null, null);
        }

        var eligibleIds = await GetEligiblePlayerIdsAsync(session, round);
        if (!eligibleIds.Contains(player.Id))
        {
            return (false, "Vous êtes spectateur pour cette manche.", null, null);
        }

        if (!eligibleIds.Contains(targetPlayerId.Value))
        {
            return (false, "Ce joueur n'est pas éligible pour cette manche.", null, null);
        }

        var alreadyUsed = await db.CopyPasteAssignments.AnyAsync(c => c.QuestionId == question.Id && c.CopierPlayerId == player.Id);
        if (alreadyUsed)
        {
            return (false, "Déjà utilisé sur cette question.", null, null);
        }

        var target = session.Players.SingleOrDefault(p => p.Id == targetPlayerId.Value);
        if (target is null)
        {
            return (false, "Joueur introuvable.", null, null);
        }

        db.CopyPasteAssignments.Add(new CopyPasteAssignment
        {
            SessionId = session.Id,
            QuestionId = question.Id,
            CopierPlayerId = player.Id,
            TargetPlayerId = target.Id,
            CreatedAt = DateTime.UtcNow
        });

        return (true, null, null, target);
    }

    /// <summary>Joker Moi d'abord : garantit d'avoir la main en premier sur les 2 prochaines questions
    /// d'un thème en mode buzzer — voir ResetPerQuestionJokerEffects pour la décrémentation et le verrou
    /// posé dans Buzz ci-dessus.</summary>
    private async Task<(bool Success, string? Error, string? Detail, Player? TargetPlayer)> UseMeFirstAsync(GameSession session, Round? round, JokerGrant grant, Player player)
    {
        if (round is null || session.CurrentQuestionStartedAt is null)
        {
            return (false, "Aucune question en cours.", null, null);
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);
        if (!engine.IsBuzzerMode(round.ConfigJson))
        {
            return (false, "Cette manche n'est pas en mode buzzer.", null, null);
        }

        var state = engine.ComputeState(round.ConfigJson, session.CurrentQuestionStartedAt.Value, session.PausedAt, DateTime.UtcNow);
        if (!state.IsAnswerWindowOpen)
        {
            return (false, "Le temps de réponse est écoulé.", null, null);
        }

        var eligibleIds = await GetEligiblePlayerIdsAsync(session, round);
        if (!eligibleIds.Contains(player.Id))
        {
            return (false, "Vous êtes spectateur pour cette manche.", null, null);
        }

        if (session.MeFirstQuestionsRemaining > 0)
        {
            return (false, "Un joker Moi d'abord est déjà actif.", null, null);
        }

        session.MeFirstHolderPlayerId = grant.PlayerId;
        session.MeFirstHolderTeamId = grant.TeamId;
        session.MeFirstQuestionsRemaining = 2;
        session.MeFirstConsumedThisQuestion = false;

        return (true, null, null, null);
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

    /// <summary>Déclenche la résolution en lot d'une feature à résolution différée (closest-guess) dès que
    /// la fenêtre de réponse est fermée — seulement en mode Auto (sinon le GM déclenche lui-même via
    /// l'endpoint dédié) et s'il reste des réponses en attente.</summary>
    private async Task<bool> ResolveDeferredScoringIfDueAsync(GameSession session, Round round, Question question, IFeatureEngine engine)
    {
        if (!engine.DefersScoringUntilWindowClose(round.ConfigJson) || !engine.ShouldAutoResolveDeferredScoring(round.ConfigJson))
        {
            return false;
        }

        var state = engine.ComputeState(round.ConfigJson, session.CurrentQuestionStartedAt!.Value, session.PausedAt, DateTime.UtcNow);
        if (state.IsAnswerWindowOpen)
        {
            return false;
        }

        var pending = await db.Answers.Where(a => a.SessionId == session.Id && a.QuestionId == question.Id && a.IsCorrect == null).ToListAsync();
        if (pending.Count == 0)
        {
            return false;
        }

        await ResolveDeferredScoringAsync(session, round, question, engine, pending);
        return true;
    }

    /// <summary>
    /// Classe les réponses en attente par proximité à la valeur cible (closest-guess) et attribue les
    /// points via la même formule que le scoring au rang (PointsForRank). En mode équipe, le classement
    /// se fait sur la MOYENNE des estimations de chaque équipe plutôt que sur chaque estimation
    /// individuelle — les joueurs sans équipe (mode équipe actif mais pas encore assignés) sont classés
    /// individuellement, pour ne pas perdre silencieusement leur estimation.
    /// </summary>
    private async Task ResolveDeferredScoringAsync(GameSession session, Round round, Question question, IFeatureEngine engine, List<Answer> pendingAnswers)
    {
        var target = engine.GetNumericTarget(question.PayloadJson);
        if (target is null)
        {
            return;
        }

        var parsed = pendingAnswers
            .Select(a => (Answer: a, Value: double.TryParse(a.RawAnswer, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (double?)v : null))
            .ToList();

        foreach (var entry in parsed.Where(p => p.Value is null))
        {
            // Estimation illisible (vide, non numérique…) : ne peut pas être classée, ne marque jamais.
            entry.Answer.IsCorrect = false;
            entry.Answer.PointsAwarded = 0;
            entry.Answer.ValidatedByGmAt = DateTime.UtcNow;
        }

        var valid = parsed.Where(p => p.Value is not null).ToList();
        var playerTeams = session.Players.ToDictionary(p => p.Id, p => p.TeamId);

        var teamEntries = session.TeamScoringEnabled
            ? valid.Where(p => playerTeams.GetValueOrDefault(p.Answer.PlayerId) is not null).ToList()
            : [];
        var soloEntries = session.TeamScoringEnabled
            ? valid.Where(p => playerTeams.GetValueOrDefault(p.Answer.PlayerId) is null).ToList()
            : valid;

        // Arrondi à 1e-6 pour grouper les égalités : évite qu'un simple bruit de virgule flottante sépare
        // deux estimations pourtant identiques en distance à la cible.
        if (teamEntries.Count > 0)
        {
            var teamGroups = teamEntries
                .GroupBy(p => playerTeams[p.Answer.PlayerId]!.Value)
                .Select(g => new { TeamId = g.Key, Entries = g.ToList(), AverageGuess = g.Average(p => p.Value!.Value) })
                .GroupBy(t => Math.Round(Math.Abs(t.AverageGuess - target.Value), 6))
                .OrderBy(g => g.Key)
                .ToList();

            var rank = 0;
            foreach (var tieGroup in teamGroups)
            {
                var points = engine.PointsForRank(round.ConfigJson, rank);
                foreach (var team in tieGroup)
                {
                    foreach (var entry in team.Entries)
                    {
                        entry.Answer.IsCorrect = points > 0;
                        entry.Answer.PointsAwarded = points;
                        entry.Answer.TeamId = team.TeamId;
                        entry.Answer.ValidatedByGmAt = DateTime.UtcNow;
                    }
                }
                // Classement "olympique" : des équipes ex æquo occupent le même rang, le rang suivant saute
                // d'autant (2 équipes à égalité au rang 0 → la suivante est au rang 2, pas 1).
                rank += tieGroup.Count();
            }
        }

        if (soloEntries.Count > 0)
        {
            var tieGroups = soloEntries
                .GroupBy(p => Math.Round(Math.Abs(p.Value!.Value - target.Value), 6))
                .OrderBy(g => g.Key)
                .ToList();

            var rank = 0;
            foreach (var tieGroup in tieGroups)
            {
                var points = engine.PointsForRank(round.ConfigJson, rank);
                foreach (var entry in tieGroup)
                {
                    entry.Answer.IsCorrect = points > 0;
                    entry.Answer.PointsAwarded = points;
                    entry.Answer.ValidatedByGmAt = DateTime.UtcNow;
                }
                rank += tieGroup.Count();
            }
        }
    }

    /// <summary>Déclenche la finalisation indépendante (order-list) dès que la fenêtre de réponse est
    /// fermée et qu'il reste des brouillons en attente — contrairement à
    /// ResolveDeferredScoringIfDueAsync, aucun classement collectif n'est nécessaire : chaque brouillon
    /// se note pour son propre compte via engine.Evaluate().</summary>
    private async Task<bool> FinalizeIndependentPendingAnswersIfDueAsync(GameSession session, Round round, Question question, IFeatureEngine engine)
    {
        if (!engine.FinalizesPendingAnswersOnAdvance(round.ConfigJson))
        {
            return false;
        }

        var state = engine.ComputeState(round.ConfigJson, session.CurrentQuestionStartedAt!.Value, session.PausedAt, DateTime.UtcNow);
        if (state.IsAnswerWindowOpen)
        {
            return false;
        }

        var pending = await db.Answers.Where(a => a.SessionId == session.Id && a.QuestionId == question.Id && a.IsCorrect == null).ToListAsync();
        if (pending.Count == 0)
        {
            return false;
        }

        await FinalizeIndependentPendingAnswersAsync(session, round, question, engine, pending);
        return true;
    }

    /// <summary>Note chaque brouillon en attente indépendamment des autres via engine.Evaluate() — utilisé
    /// à la fermeture de fenêtre (poll, voir ci-dessus) et systématiquement avant de quitter une question
    /// (AdvanceToNextQuestionAsync), en filet de sécurité pour le cas où le GM cliquerait "Suivant" avant
    /// qu'aucun poll n'ait eu l'occasion de résoudre les brouillons restants.</summary>
    private async Task FinalizeIndependentPendingAnswersAsync(GameSession session, Round round, Question question, IFeatureEngine engine, List<Answer>? pendingAnswers = null)
    {
        pendingAnswers ??= await db.Answers.Where(a => a.SessionId == session.Id && a.QuestionId == question.Id && a.IsCorrect == null).ToListAsync();

        foreach (var answer in pendingAnswers)
        {
            var evaluation = engine.Evaluate(round.ConfigJson, question.PayloadJson, answer.RawAnswer, session.CurrentQuestionStartedAt!.Value, session.PausedAt, DateTime.UtcNow);
            answer.IsCorrect = evaluation.IsCorrect;
            // Contrairement à un simple qa-text, evaluation.PointsAwarded peut être non-nul même si
            // IsCorrect est faux (order-list : crédit partiel selon la chaîne bien enchaînée) — on
            // n'écrase donc jamais ici, la valeur renvoyée par l'engine est déjà la bonne.
            answer.PointsAwarded = evaluation.PointsAwarded;
            answer.ValidatedByGmAt = DateTime.UtcNow;
        }
    }

    /// <summary>Déclenche la résolution du joker Copier/coller dès que la fenêtre de réponse est fermée
    /// (même timing que FinalizeIndependentPendingAnswersIfDueAsync, dont dépend le résultat pour
    /// order-list : la réponse de la cible doit déjà être finalisée avant d'être copiée).</summary>
    private async Task<bool> ResolveCopyPasteAssignmentsIfDueAsync(GameSession session, Round round, Question question, IFeatureEngine engine)
    {
        var state = engine.ComputeState(round.ConfigJson, session.CurrentQuestionStartedAt!.Value, session.PausedAt, DateTime.UtcNow);
        if (state.IsAnswerWindowOpen)
        {
            return false;
        }

        return await ResolveCopyPasteAssignmentsAsync(session, round, question);
    }

    /// <summary>Applique chaque assignation Copier/coller en attente sur cette question : la réponse du
    /// copieur (créée si besoin) devient une copie exacte de celle du joueur ciblé — verdict et points
    /// compris. Si la cible n'a pas répondu, le copieur reste sans réponse (rien à copier). Utilisé à la
    /// fermeture de fenêtre (poll) et systématiquement avant de quitter une question, en filet de sécurité
    /// pour le cas où le GM cliquerait "Suivant" avant qu'aucun poll n'ait eu l'occasion de résoudre.</summary>
    private async Task<bool> ResolveCopyPasteAssignmentsAsync(GameSession session, Round round, Question question)
    {
        if (!SimultaneousAnswerFeatures.Contains(round.FeatureTypeKey))
        {
            return false;
        }

        var assignments = await db.CopyPasteAssignments.Where(c => c.SessionId == session.Id && c.QuestionId == question.Id).ToListAsync();
        if (assignments.Count == 0)
        {
            return false;
        }

        foreach (var assignment in assignments)
        {
            var targetAnswer = await db.Answers
                .Where(a => a.SessionId == session.Id && a.QuestionId == question.Id && a.PlayerId == assignment.TargetPlayerId)
                .OrderByDescending(a => a.SubmittedAt)
                .FirstOrDefaultAsync();

            if (targetAnswer is not null)
            {
                var copier = session.Players.SingleOrDefault(p => p.Id == assignment.CopierPlayerId);
                var copierAnswer = await db.Answers
                    .Where(a => a.SessionId == session.Id && a.QuestionId == question.Id && a.PlayerId == assignment.CopierPlayerId)
                    .OrderByDescending(a => a.SubmittedAt)
                    .FirstOrDefaultAsync();

                if (copierAnswer is null)
                {
                    copierAnswer = new Answer
                    {
                        SessionId = session.Id,
                        PlayerId = assignment.CopierPlayerId,
                        QuestionId = question.Id,
                        RawAnswer = targetAnswer.RawAnswer,
                        ValidationMode = targetAnswer.ValidationMode,
                        SubmittedAt = DateTime.UtcNow
                    };
                    db.Answers.Add(copierAnswer);
                }
                else
                {
                    copierAnswer.RawAnswer = targetAnswer.RawAnswer;
                }

                copierAnswer.IsCorrect = targetAnswer.IsCorrect;
                copierAnswer.PendingPoints = targetAnswer.PendingPoints;
                copierAnswer.PointsAwarded = targetAnswer.PointsAwarded;
                copierAnswer.TeamId = session.TeamScoringEnabled ? copier?.TeamId : null;
                copierAnswer.ValidatedByGmAt = DateTime.UtcNow;
            }

            db.CopyPasteAssignments.Remove(assignment);
        }

        return true;
    }

    private async Task<bool> FastForwardIfAllPlayersAnswered(GameSession session, Round round, Question question, IFeatureEngine engine)
    {
        // Le temps est gelé pour une revue GM en cours (validation manuelle ou buzzer) : ne pas
        // recalculer CurrentQuestionStartedAt tant que ce gel n'a pas été levé, sous peine de le
        // réécrire en boucle à chaque sondage tant que PausedAt reste posé.
        if (session.PausedAt is not null)
        {
            return false;
        }

        // Feature à résolution différée (closest-guess) : "trouvé" n'a pas de sens avant la résolution en
        // lot (IsCorrect reste null pour tout le monde) — le critère de complétion est juste "tout le
        // monde a soumis une estimation", pas "tout le monde a la bonne réponse".
        var allAnswered = engine.DefersScoringUntilWindowClose(round.ConfigJson)
            ? await AllEligiblePlayersSubmittedAsync(session, round, question)
            : await AllPlayersAnsweredCorrectly(session, round, question);

        if (!allAnswered)
        {
            return false;
        }

        var target = engine.GetFastForwardTargetElapsedSeconds(round.ConfigJson);
        var elapsed = SessionTiming.ComputeElapsedSeconds(session.CurrentQuestionStartedAt!.Value, session.PausedAt, DateTime.UtcNow);
        if (elapsed >= target)
        {
            return false;
        }

        session.CurrentQuestionStartedAt = DateTime.UtcNow.AddSeconds(-target);
        return true;
    }

    /// <summary>
    /// Un joueur qui s'est déjà trompé sur cette question peut-il retenter sa chance maintenant ?
    /// Vrai immédiatement si la manche l'autorise sans délai (retry écrit classique). En mode buzzer
    /// avec un délai configuré, vrai seulement une fois le délai écoulé — sauf si tous les autres
    /// joueurs ont déjà utilisé leur tentative, auquel cas plus personne ne peut le devancer de toute façon.
    /// </summary>
    private async Task<bool> CanPlayerRetryAsync(GameSession session, Question question, IFeatureEngine engine, string configJson, Answer lastAnswer)
    {
        if (lastAnswer.IsCorrect != false)
        {
            return false;
        }

        if (!engine.AllowsRetryAfterWrongAnswer(configJson))
        {
            return false;
        }

        var cooldown = engine.GetRetryCooldownSeconds(configJson);
        if (cooldown <= 0)
        {
            return true;
        }

        var elapsedSinceWrongAnswer = (DateTime.UtcNow - lastAnswer.SubmittedAt).TotalSeconds;
        if (elapsedSinceWrongAnswer >= cooldown)
        {
            return true;
        }

        var otherPlayerIds = session.Players.Where(p => p.Id != lastAnswer.PlayerId).Select(p => p.Id).ToList();
        if (otherPlayerIds.Count == 0)
        {
            return true;
        }

        var answeredOtherIds = await db.Answers
            .Where(a => a.QuestionId == question.Id && otherPlayerIds.Contains(a.PlayerId))
            .Select(a => a.PlayerId)
            .Distinct()
            .ToListAsync();

        return otherPlayerIds.All(answeredOtherIds.Contains);
    }

    /// <summary>
    /// Points qu'une réponse rapporterait si elle était jugée correcte maintenant. En scoring au rang, le rang
    /// est le nombre de bonnes réponses déjà enregistrées pour cette question (0 = premier) ; sinon la valeur
    /// classique (palier de zoom / points fixes) au temps écoulé donné.
    /// </summary>
    private async Task<int> ComputePointsIfCorrect(IFeatureEngine engine, string configJson, int sessionId, int questionId, double elapsedSeconds)
    {
        if (engine.UsesRankBasedScoring(configJson))
        {
            var rank = await db.Answers.CountAsync(a => a.SessionId == sessionId && a.QuestionId == questionId && a.IsCorrect == true);
            return engine.PointsForRank(configJson, rank);
        }

        return engine.PointsForElapsedSeconds(configJson, elapsedSeconds);
    }

    /// <summary>Gèle le minuteur pendant qu'une réponse attend le jugement du GM (validation manuelle ou buzzer).
    /// Sans effet si déjà en pause (GM ou une autre réponse en attente) : on ne déplace pas le point de gel.</summary>
    private static void PauseForPendingReview(GameSession session) => session.PausedAt ??= DateTime.UtcNow;

    /// <summary>Reprend le minuteur là où il s'était arrêté, mais seulement si plus aucune réponse de cette
    /// question n'attend de jugement et que personne ne tient le buzzer — sinon une autre réponse est encore
    /// en cours d'examen et le temps doit rester gelé.</summary>
    private async Task ResumeAfterReviewIfClearAsync(GameSession session, int questionId, int? excludingAnswerId = null)
    {
        if (session.Status != GameSessionStatus.Running || session.PausedAt is null)
        {
            return;
        }

        // excludingAnswerId : la réponse en cours de jugement par l'appelant n'est pas encore
        // persistée en base à cet instant (SaveChangesAsync n'a pas encore été appelé) — sans cette
        // exclusion, la requête la trouverait toujours "en attente" (IsCorrect NULL côté base) et le
        // minuteur resterait bloqué en pause indéfiniment.
        var stillPending = await db.Answers
            .AnyAsync(a => a.SessionId == session.Id && a.QuestionId == questionId && a.IsCorrect == null && a.Id != excludingAnswerId);
        if (stillPending || session.CurrentBuzzHolderPlayerId is not null)
        {
            return;
        }

        var pausedDuration = DateTime.UtcNow - session.PausedAt.Value;
        session.CurrentQuestionStartedAt = session.CurrentQuestionStartedAt!.Value + pausedDuration;
        session.PausedAt = null;
    }

    private async Task<Answer?> GetLastAnswer(int playerId, int questionId) =>
        await db.Answers
            .Where(a => a.PlayerId == playerId && a.QuestionId == questionId)
            .OrderByDescending(a => a.SubmittedAt)
            .FirstOrDefaultAsync();

    private async Task<List<string>> GetCorrectFinderPseudos(int sessionId, int questionId) =>
        await db.Answers
            .Where(a => a.SessionId == sessionId && a.QuestionId == questionId && a.IsCorrect == true)
            .OrderBy(a => a.SubmittedAt)
            .Select(a => a.Player!.Pseudo)
            .ToListAsync();

    /// <summary>order-list uniquement : la ligne Answer (brouillon ou déjà finalisée) partagée par le
    /// groupe du joueur pour cette question — toute l'équipe si le mode équipe est actif et que le joueur
    /// en a une, sinon le joueur seul. Dernière ligne par SubmittedAt au cas où (ne devrait normalement
    /// jamais y en avoir plus d'une par groupe/question, la logique d'écriture met toujours à jour la
    /// ligne existante plutôt que d'en insérer une nouvelle).</summary>
    private async Task<Answer?> GetOrderListGroupAnswerAsync(GameSession session, int questionId, Player player)
    {
        var groupTeamId = session.TeamScoringEnabled ? player.TeamId : null;

        return groupTeamId is not null
            ? await db.Answers
                .Where(a => a.SessionId == session.Id && a.QuestionId == questionId && a.TeamId == groupTeamId)
                .OrderByDescending(a => a.SubmittedAt)
                .FirstOrDefaultAsync()
            : await db.Answers
                .Where(a => a.SessionId == session.Id && a.QuestionId == questionId && a.PlayerId == player.Id && a.TeamId == null)
                .OrderByDescending(a => a.SubmittedAt)
                .FirstOrDefaultAsync();
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
            await ResolveCopyPasteAssignmentsAsync(session, leavingRound, leavingQuestion);
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

    private async Task BroadcastState(GameSession session, Quiz quiz)
    {
        await hub.Clients.Group(session.InviteToken).SendAsync("StateChanged", await BuildStateDto(session, quiz));
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
            session.MeFirstConsumedThisQuestion);
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
