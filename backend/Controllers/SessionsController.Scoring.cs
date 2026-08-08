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
        var round = answer.Question?.Round;
        var engine = round is null ? null : engineRegistry.Get(round.FeatureTypeKey);
        var usesRankBasedScoring = engine is not null && round is not null && engine.UsesRankBasedScoring(round.ConfigJson);

        answer.IsCorrect = request.IsCorrect;
        answer.ValidatedByGmAt = DateTime.UtcNow;

        var (session, _) = loaded.Value;
        var affectedPlayerIds = new List<int> { answer.PlayerId };

        if (usesRankBasedScoring)
        {
            // Le rang de chaque réponse dépend du nombre d'AUTRES réponses déjà validées correctes sur
            // cette question — invalider/revalider une réponse peut donc décaler le rang (et les points)
            // de réponses déjà jugées. On recalcule tout le lot plutôt que de figer un rang au fil de l'eau,
            // sinon les réponses validées avant ce changement restent bloquées sur un rang périmé.
            var correctAnswers = await db.Answers
                .Where(a => a.SessionId == id && a.QuestionId == answer.QuestionId && a.IsCorrect == true)
                .OrderBy(a => a.ValidatedByGmAt)
                .ToListAsync();

            for (var i = 0; i < correctAnswers.Count; i++)
            {
                correctAnswers[i].PointsAwarded = engine!.PointsForRank(round!.ConfigJson, i);
            }

            affectedPlayerIds = correctAnswers.Select(a => a.PlayerId).Distinct().ToList();
            if (!request.IsCorrect)
            {
                answer.PointsAwarded = 0;
                if (!affectedPlayerIds.Contains(answer.PlayerId))
                {
                    affectedPlayerIds.Add(answer.PlayerId);
                }
            }
        }
        else
        {
            answer.PointsAwarded = request.IsCorrect ? answer.PendingPoints : 0;
        }

        if (wasPending)
        {
            // Ne reprend le minuteur que si c'était la dernière réponse en attente de jugement pour cette question.
            await ResumeAfterReviewIfClearAsync(session, answer.QuestionId, excludingAnswerId: answer.Id);
        }

        await db.SaveChangesAsync();

        PlayerDto? validatedPlayerDto = null;
        foreach (var playerId in affectedPlayerIds)
        {
            var dto = await BuildPlayerDto(playerId, session.Id, playerId == answer.PlayerId ? answer.Player?.Pseudo : null);
            await hub.Clients.Group(session.InviteToken).SendAsync("ScoreUpdated", dto);
            if (playerId == answer.PlayerId)
            {
                validatedPlayerDto = dto;
            }
        }

        return Ok(validatedPlayerDto ?? await BuildPlayerDto(answer.PlayerId, session.Id, answer.Player?.Pseudo));
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
        var points = await ComputePointsIfCorrect(engine, question.PayloadJson, round.ConfigJson, session.Id, question.Id, 0);
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
        // Caché tant que la fenêtre de réponse est ouverte : révéler en direct qui a déjà (bien) répondu
        // trahit l'information et fausse les décisions des jokers Copier/coller / Seul au monde (voir qui
        // répond avant de choisir sa cible ou d'utiliser le joker). La vue GM (GetCurrentQuestionFull) n'a
        // pas cette restriction — l'hôte doit toujours tout voir en direct.
        var correctFinders = state.IsAnswerWindowOpen ? [] : await GetCorrectFinderPseudos(session.Id, question.Id);
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
        var pendingPoints = await ComputePointsIfCorrect(engine, question.PayloadJson, round.ConfigJson, session.Id, question.Id, elapsedSeconds);
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

    private async Task<bool> AllPlayersAnsweredCorrectly(GameSession session, Round round, Question question)
    {
        var eligiblePlayerIds = await GetEligiblePlayerIdsAsync(session, round);
        if (eligiblePlayerIds.Count == 0)
        {
            return false;
        }

        // Une réponse comblée par le joker Copier/coller ne compte jamais comme "réponse obtenue" pour
        // cette avance automatique : c'est une révélation différée, pas une vraie réponse à temps — sans
        // cette exclusion, sa résolution (à la fermeture de fenêtre) peut compléter silencieusement "tout
        // le monde a répondu" et faire avancer la partie sans que le GM ait rien décidé.

        // Mode buzzer : une course, pas un test collectif — une seule bonne réponse clôt la question
        // (mais toujours restreinte aux participants éligibles de cette manche).
        if (!engineRegistry.Get(round.FeatureTypeKey).RequiresAllPlayersToAnswer(round.ConfigJson))
        {
            return await db.Answers.AnyAsync(a => a.SessionId == session.Id && a.QuestionId == question.Id
                && a.IsCorrect == true && !a.IsFromCopyPasteJoker && eligiblePlayerIds.Contains(a.PlayerId));
        }

        var correctCount = await db.Answers.CountAsync(a => a.SessionId == session.Id && a.QuestionId == question.Id
            && a.IsCorrect == true && !a.IsFromCopyPasteJoker && eligiblePlayerIds.Contains(a.PlayerId));
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
    private async Task<int> ComputePointsIfCorrect(IFeatureEngine engine, string payloadJson, string configJson, int sessionId, int questionId, double elapsedSeconds)
    {
        var fixedPoints = engine.FixedManualValidationPoints(payloadJson, configJson);
        if (fixedPoints is not null)
        {
            return fixedPoints.Value;
        }

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
}
