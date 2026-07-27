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

        EnterRound(quiz.Rounds.OrderBy(r => r.Order).ToList(), session, 0);

        await db.SaveChangesAsync();
        await BroadcastState(session, quiz);

        return Ok(await BuildStateDto(session, quiz));
    }

    [Authorize]
    [HttpPost("{id:int}/round-target-player")]
    public async Task<ActionResult<GameSessionStateDto>> SetRoundTargetPlayer(int id, SetRoundTargetPlayerRequest request)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;
        if (session.Status != GameSessionStatus.AwaitingTargetPlayer)
        {
            return BadRequest("La session n'attend pas la désignation d'un joueur.");
        }

        var player = session.Players.SingleOrDefault(p => p.Id == request.PlayerId);
        if (player is null)
        {
            return BadRequest("Joueur introuvable dans cette session.");
        }

        session.CurrentRoundTargetPlayerId = player.Id;
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
        if (session.Status != GameSessionStatus.AwaitingTargetPlayer)
        {
            return BadRequest("La session n'attend pas la désignation d'un joueur.");
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
        if (session.Status is not (GameSessionStatus.Running or GameSessionStatus.Paused or GameSessionStatus.RoundIntermission))
        {
            return BadRequest("La session n'est pas en cours.");
        }

        // Action explicite du GM : autorisée à franchir la frontière d'une manche
        // (contrairement à l'auto-advance, qui doit toujours s'arrêter en fin de manche).
        AdvanceToNextQuestion(quiz, session, crossRoundBoundary: true);

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

        return Ok(new CurrentQuestionAdminDto(
            round.Id, round.Title, round.FeatureTypeKey,
            question.Id, question.PayloadJson, round.ConfigJson,
            state.CurrentLevel, state.CurrentPoints, state.SecondsRemainingInStep, state.SecondsRemainingTotal, state.IsAnswerWindowOpen,
            engine.IsBuzzerMode(round.ConfigJson), correctFinders));
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
                var rank = await db.Answers.CountAsync(a => a.QuestionId == answer.QuestionId && a.IsCorrect == true && a.Id != answer.Id);
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
        var points = await ComputePointsIfCorrect(engine, round.ConfigJson, question.Id, 0);

        db.Answers.Add(new Answer
        {
            SessionId = session.Id,
            PlayerId = holderId,
            QuestionId = question.Id,
            RawAnswer = "(buzzer)",
            IsCorrect = request.IsCorrect,
            PendingPoints = points,
            PointsAwarded = request.IsCorrect ? points : 0,
            ValidationMode = AnswerValidationMode.Manual,
            SubmittedAt = DateTime.UtcNow,
            ValidatedByGmAt = DateTime.UtcNow
        });

        session.CurrentBuzzHolderPlayerId = null;
        await ResumeAfterReviewIfClearAsync(session, question.Id);
        await db.SaveChangesAsync();

        if (request.IsCorrect)
        {
            var playerDto = await BuildPlayerDto(holderId, session.Id, null);
            await hub.Clients.Group(session.InviteToken).SendAsync("ScoreUpdated", playerDto);
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

        await hub.Clients.Group(session.InviteToken).SendAsync("PlayerJoined", new PlayerDto(player.Id, player.Pseudo, 0));

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
        var publicPayloadJson = engine.BuildPublicPayloadJson(question.PayloadJson);

        var player = await db.Players.SingleOrDefaultAsync(p => p.SessionId == session.Id && p.ConnectionToken == connectionToken);
        var lastAnswer = player is null ? null : await GetLastAnswer(player.Id, question.Id);
        // Bloqué si aucune tentative n'est en cours (correcte ou en attente de validation manuelle),
        // ou si la dernière tentative était fausse mais qu'aucun nouvel essai n'est encore permis.
        var hasAnswered = lastAnswer is not null && !await CanPlayerRetryAsync(session, question, engine, round.ConfigJson, lastAnswer);
        var correctFinders = await GetCorrectFinderPseudos(session.Id, question.Id);
        var isSpectator = round.RequiresTargetPlayer && (player is null || session.CurrentRoundTargetPlayerId != player.Id);

        return Ok(new PlayerQuestionDto(
            question.Id, round.Title, round.FeatureTypeKey, publicPayloadJson,
            state.CurrentLevel, state.SecondsRemainingInStep, state.SecondsRemainingTotal, state.IsAnswerWindowOpen, hasAnswered, correctFinders, isSpectator,
            engine.IsBuzzerMode(round.ConfigJson)));
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

        if (round.RequiresTargetPlayer && session.CurrentRoundTargetPlayerId != player.Id)
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

        if (round.RequiresTargetPlayer && session.CurrentRoundTargetPlayerId != player.Id)
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Vous êtes spectateur pour cette manche.");
        }

        var engine = engineRegistry.Get(round.FeatureTypeKey);
        if (engine.IsBuzzerMode(round.ConfigJson))
        {
            return BadRequest("Cette question se joue au buzzer : utilisez le bouton dédié.");
        }

        var lastAnswer = await GetLastAnswer(player.Id, question.Id);
        if (lastAnswer is not null && !await CanPlayerRetryAsync(session, question, engine, round.ConfigJson, lastAnswer))
        {
            return Conflict("Réponse déjà envoyée pour cette question.");
        }

        var submittedAt = DateTime.UtcNow;

        var elapsedSeconds = SessionTiming.ComputeElapsedSeconds(session.CurrentQuestionStartedAt.Value, session.PausedAt, submittedAt);
        // Calculé AVANT d'insérer cette réponse : en scoring au rang, le rang doit exclure la tentative en cours.
        var pendingPoints = await ComputePointsIfCorrect(engine, round.ConfigJson, question.Id, elapsedSeconds);
        var evaluation = engine.Evaluate(round.ConfigJson, question.PayloadJson, request.RawAnswer, session.CurrentQuestionStartedAt.Value, session.PausedAt, submittedAt);
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
            ValidationMode = engine.IsManualValidation(round.ConfigJson) ? AnswerValidationMode.Manual : AnswerValidationMode.Auto,
            SubmittedAt = submittedAt
        };

        db.Answers.Add(answer);

        if (evaluation.IsCorrect is null)
        {
            // Le temps s'arrête tant que le GM n'a pas jugé cette réponse en attente de validation manuelle.
            PauseForPendingReview(session);
        }

        await db.SaveChangesAsync();

        if (evaluation.IsCorrect is not null)
        {
            var playerDto = await BuildPlayerDto(player.Id, session.Id, player.Pseudo);
            await hub.Clients.Group(session.InviteToken).SendAsync("ScoreUpdated", playerDto);
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
        var session = await db.GameSessions.Include(s => s.Players).SingleOrDefaultAsync(s => s.Id == sessionId);
        if (session is null)
        {
            return null;
        }

        var quiz = await db.Quizzes
            .Include(q => q.Rounds).ThenInclude(r => r.Questions)
            .SingleOrDefaultAsync(q => q.Id == session.QuizId && q.OwnerId == ownerId);

        return quiz is null ? null : (session, quiz);
    }

    private async Task<(GameSession session, Quiz quiz)?> LoadSessionByToken(string token)
    {
        var session = await db.GameSessions.Include(s => s.Players).SingleOrDefaultAsync(s => s.InviteToken == token);
        if (session is null)
        {
            return null;
        }

        var quiz = await db.Quizzes
            .Include(q => q.Rounds).ThenInclude(r => r.Questions)
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

        var state = engine.ComputeState(round.ConfigJson, session.CurrentQuestionStartedAt!.Value, session.PausedAt, DateTime.UtcNow);
        var allAnswered = await AllPlayersAnsweredCorrectly(session, round, question);

        // Une fois que tout le monde a trouvé, la question se termine automatiquement même si
        // l'auto-advance n'est pas coché sur la manche : il n'y a plus personne pour répondre.
        if (state.ShouldAutoAdvance || (allAnswered && !state.IsAnswerWindowOpen))
        {
            // Ne franchit jamais une frontière de manche tout seul, seul le GM en décide.
            AdvanceToNextQuestion(quiz, session, crossRoundBoundary: false);
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
        // Manche ciblée : seul le joueur désigné compte, les spectateurs ne peuvent pas répondre.
        if (round.RequiresTargetPlayer)
        {
            return session.CurrentRoundTargetPlayerId is not null &&
                await db.Answers.AnyAsync(a => a.SessionId == session.Id && a.QuestionId == question.Id
                    && a.PlayerId == session.CurrentRoundTargetPlayerId && a.IsCorrect == true);
        }

        // Mode buzzer : une course, pas un test collectif — une seule bonne réponse clôt la question.
        if (!engineRegistry.Get(round.FeatureTypeKey).RequiresAllPlayersToAnswer(round.ConfigJson))
        {
            return await db.Answers.AnyAsync(a => a.SessionId == session.Id && a.QuestionId == question.Id && a.IsCorrect == true);
        }

        if (session.Players.Count == 0)
        {
            return false;
        }

        var correctCount = await db.Answers.CountAsync(a => a.SessionId == session.Id && a.QuestionId == question.Id && a.IsCorrect == true);
        return correctCount >= session.Players.Count;
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

        if (!await AllPlayersAnsweredCorrectly(session, round, question))
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
    private async Task<int> ComputePointsIfCorrect(IFeatureEngine engine, string configJson, int questionId, double elapsedSeconds)
    {
        if (engine.UsesRankBasedScoring(configJson))
        {
            var rank = await db.Answers.CountAsync(a => a.QuestionId == questionId && a.IsCorrect == true);
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
            .AnyAsync(a => a.QuestionId == questionId && a.IsCorrect == null && a.Id != excludingAnswerId);
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

    private static (Round? round, Question? question) GetCurrentRoundAndQuestion(Quiz quiz, GameSession session)
    {
        var rounds = quiz.Rounds.OrderBy(r => r.Order).ToList();
        if (session.CurrentRoundIndex < 0 || session.CurrentRoundIndex >= rounds.Count)
        {
            return (null, null);
        }

        var round = rounds[session.CurrentRoundIndex];
        var questions = round.Questions.OrderBy(q => q.Order).ToList();
        if (session.CurrentQuestionIndex < 0 || session.CurrentQuestionIndex >= questions.Count)
        {
            return (round, null);
        }

        return (round, questions[session.CurrentQuestionIndex]);
    }

    private static void AdvanceToNextQuestion(Quiz quiz, GameSession session, bool crossRoundBoundary)
    {
        var rounds = quiz.Rounds.OrderBy(r => r.Order).ToList();
        var currentRound = session.CurrentRoundIndex >= 0 && session.CurrentRoundIndex < rounds.Count
            ? rounds[session.CurrentRoundIndex]
            : null;
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
            EnterRound(rounds, session, session.CurrentRoundIndex + 1);
            return;
        }
        else
        {
            session.Status = GameSessionStatus.Finished;
            session.CurrentQuestionStartedAt = null;
            return;
        }

        session.Status = GameSessionStatus.Running;
        session.CurrentQuestionStartedAt = DateTime.UtcNow;
        session.PausedAt = null;
        session.CurrentBuzzHolderPlayerId = null;
    }

    /// <summary>Positionne la session au début d'une manche : démarre directement si la manche est libre,
    /// ou s'arrête en AwaitingTargetPlayer si elle est réservée à un joueur (Round.RequiresTargetPlayer),
    /// en attendant que le GM le désigne.</summary>
    private static void EnterRound(List<Round> rounds, GameSession session, int roundIndex)
    {
        session.CurrentRoundIndex = roundIndex;
        session.CurrentQuestionIndex = 0;
        session.CurrentRoundTargetPlayerId = null;
        session.CurrentBuzzHolderPlayerId = null;
        session.PausedAt = null;

        var round = rounds[roundIndex];
        if (round.RequiresTargetPlayer)
        {
            session.Status = GameSessionStatus.AwaitingTargetPlayer;
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
        pseudo ??= (await db.Players.SingleAsync(p => p.Id == playerId)).Pseudo;
        var score = await ComputeScore(playerId, sessionId);
        return new PlayerDto(playerId, pseudo, score);
    }

    private async Task<int> ComputeScore(int playerId, int sessionId)
    {
        var answerPoints = await db.Answers
            .Where(a => a.PlayerId == playerId && a.SessionId == sessionId)
            .SumAsync(a => (int?)a.PointsAwarded) ?? 0;

        var adjustmentPoints = await db.ScoreAdjustments
            .Where(a => a.PlayerId == playerId && a.SessionId == sessionId)
            .SumAsync(a => (int?)a.Delta) ?? 0;

        return answerPoints + adjustmentPoints;
    }

    private async Task<GameSessionStateDto> BuildStateDto(GameSession session, Quiz quiz)
    {
        var scores = await ComputeAllScores(session.Id);

        var players = session.Players
            .Select(p => new PlayerDto(p.Id, p.Pseudo, scores.GetValueOrDefault(p.Id)))
            .OrderByDescending(p => p.Score)
            .ToList();

        var targetPlayerPseudo = session.CurrentRoundTargetPlayerId is null
            ? null
            : session.Players.SingleOrDefault(p => p.Id == session.CurrentRoundTargetPlayerId)?.Pseudo;

        var buzzHolderPseudo = session.CurrentBuzzHolderPlayerId is null
            ? null
            : session.Players.SingleOrDefault(p => p.Id == session.CurrentBuzzHolderPlayerId)?.Pseudo;

        return new GameSessionStateDto(
            session.Id, session.InviteToken, quiz.Title, session.Status,
            session.CurrentRoundIndex, session.CurrentQuestionIndex, quiz.Rounds.Count, session.ScoreboardVisible,
            session.CurrentRoundTargetPlayerId, targetPlayerPseudo,
            session.CurrentBuzzHolderPlayerId, buzzHolderPseudo, players);
    }

    private async Task<Dictionary<int, int>> ComputeAllScores(int sessionId)
    {
        var answerScores = await db.Answers
            .Where(a => a.SessionId == sessionId)
            .GroupBy(a => a.PlayerId)
            .Select(g => new { PlayerId = g.Key, Total = g.Sum(a => a.PointsAwarded) })
            .ToDictionaryAsync(x => x.PlayerId, x => x.Total);

        var adjustmentScores = await db.ScoreAdjustments
            .Where(a => a.SessionId == sessionId)
            .GroupBy(a => a.PlayerId)
            .Select(g => new { PlayerId = g.Key, Total = g.Sum(a => a.Delta) })
            .ToDictionaryAsync(x => x.PlayerId, x => x.Total);

        var result = new Dictionary<int, int>(answerScores);
        foreach (var (playerId, total) in adjustmentScores)
        {
            result[playerId] = result.GetValueOrDefault(playerId) + total;
        }

        return result;
    }
}
