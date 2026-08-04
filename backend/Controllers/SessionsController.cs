using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QuizParty.Api.Data;
using QuizParty.Api.Dtos;
using QuizParty.Api.Extensions;
using QuizParty.Api.Features.Shared;
using QuizParty.Api.Hubs;
using QuizParty.Api.Models;
using QuizParty.Api.Services;

namespace QuizParty.Api.Controllers;

[ApiController]
[Route("api/sessions")]
public class SessionsController(QuizPartyDbContext db, FeatureEngineRegistry engineRegistry, IHubContext<GameHub> hub) : ControllerBase
{
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
        session.CurrentBuzzHolderPlayerId = null;

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

        return Ok(new CurrentQuestionAdminDto(
            round.Id, round.Title, round.FeatureTypeKey,
            question.Id, adminPayloadJson, round.ConfigJson,
            state.CurrentLevel, state.CurrentPoints, state.SecondsRemainingInStep, state.SecondsRemainingTotal, state.IsAnswerWindowOpen,
            engine.IsBuzzerMode(round.ConfigJson), correctFinders, state.SecondsElapsedTotal, session.PausedAt is not null,
            awaitingDeferredResolution));
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
        var publicPayloadJson = engine.BuildPublicPayloadJson(effectivePayloadForPlayer);

        var player = await db.Players.SingleOrDefaultAsync(p => p.SessionId == session.Id && p.ConnectionToken == connectionToken);
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

        return Ok(new PlayerQuestionDto(
            question.Id, round.Title, round.FeatureTypeKey, publicPayloadJson,
            state.CurrentLevel, state.SecondsRemainingInStep, state.SecondsRemainingTotal, state.IsAnswerWindowOpen, hasAnswered, correctFinders, isSpectator,
            engine.IsBuzzerMode(round.ConfigJson), state.SecondsElapsedTotal, session.PausedAt is not null,
            lastAnswer?.IsCorrect, lastAnswer?.IsCorrect is not null ? lastAnswer.PointsAwarded : null,
            closestGuessEntries, closestGuessTargetValue));
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

        var lastAnswer = await GetLastAnswer(player.Id, question.Id);
        if (lastAnswer is not null && !await CanPlayerRetryAsync(session, question, engine, round.ConfigJson, lastAnswer))
        {
            return Conflict("Vous avez déjà utilisé votre tentative sur cette question.");
        }

        session.CurrentBuzzHolderPlayerId = player.Id;
        // Le temps s'arrête tant que le GM n'a pas jugé la réponse orale du joueur qui a la main.
        PauseForPendingReview(session);
        await db.SaveChangesAsync();
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
        var pointsAwarded = evaluation.IsCorrect == true ? pendingPoints : 0;

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
        if (session is null)
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
                session.CurrentBuzzHolderPlayerId = null;
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
            session.CurrentBuzzHolderPlayerId = null;
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
            session.CurrentBuzzHolderPlayerId = null;
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
            session.CurrentBuzzHolderPlayerId = null;
            session.CurrentAnswererPlayerId = null;

            var staleParticipants = await db.RoundParticipants.Where(rp => rp.SessionId == session.Id).ToListAsync();
            db.RoundParticipants.RemoveRange(staleParticipants);
            return;
        }

        session.Status = GameSessionStatus.Running;
        session.CurrentQuestionStartedAt = DateTime.UtcNow;
        session.PausedAt = null;
        session.CurrentBuzzHolderPlayerId = null;
    }

    /// <summary>Positionne la session au début d'une manche : démarre directement si elle est libre, s'arrête
    /// en AwaitingParticipants si elle est restreinte (Round.RestrictsParticipants) en attendant que le GM
    /// désigne les participants, ou passe en ChoosingTheme si c'est une manche à thèmes (le plateau est
    /// affiché, chaque sous-manche repart d'un état vierge : non révélée, en attente).</summary>
    private async Task EnterRoundAsync(List<Round> rounds, GameSession session, int roundIndex)
    {
        session.CurrentRoundIndex = roundIndex;
        session.CurrentBuzzHolderPlayerId = null;
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

        return new GameSessionStateDto(
            session.Id, session.InviteToken, quiz.Title, session.Status,
            session.CurrentRoundIndex, session.CurrentQuestionIndex, topLevelRounds.Count, session.ScoreboardVisible,
            participantPlayerIds, participantTeamIds, session.TeamScoringEnabled,
            session.CurrentBuzzHolderPlayerId, buzzHolderPseudo, players, teams, themeBoard,
            session.CurrentAnswererPlayerId, answererPseudo);
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
