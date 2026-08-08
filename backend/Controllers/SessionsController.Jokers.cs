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

        // Un seul Échange réussi par thème : sans cette garde, une équipe adverse pourrait revoler la
        // désignation juste après, dans une guerre de vol sans fin qui gaspille les charges des deux côtés.
        if (session.ExchangeUsedForThemeSubRoundId == session.CurrentThemeSubRoundId)
        {
            return (false, "Échange déjà utilisé sur ce thème.", null, null);
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

        session.ExchangeUsedForThemeSubRoundId = session.CurrentThemeSubRoundId;

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

        return await ResolveCopyPasteAssignmentsAsync(session, round, question, engine);
    }

    /// <summary>Applique chaque assignation Copier/coller en attente sur cette question : la réponse du
    /// copieur (créée si besoin) devient une copie exacte de celle du joueur ciblé — verdict et points
    /// compris. Si la cible n'a pas répondu, le copieur reste sans réponse (rien à copier). Utilisé à la
    /// fermeture de fenêtre (poll) et systématiquement avant de quitter une question, en filet de sécurité
    /// pour le cas où le GM cliquerait "Suivant" avant qu'aucun poll n'ait eu l'occasion de résoudre.</summary>
    private async Task<bool> ResolveCopyPasteAssignmentsAsync(GameSession session, Round round, Question question, IFeatureEngine engine)
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

        var usesRankBasedScoring = engine.UsesRankBasedScoring(round.ConfigJson);

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
                copierAnswer.IsFromCopyPasteJoker = true;

                // En dégressif par rang, le copieur n'hérite QUE de la réponse/du verdict de la cible —
                // ses points dépendent de SON PROPRE rang au moment où sa copie se résout, jamais d'un
                // clone verbatim des points de la cible (qui a pu répondre bien plus tôt, à un rang
                // meilleur).
                if (usesRankBasedScoring && targetAnswer.IsCorrect == true)
                {
                    var rank = await db.Answers.CountAsync(a => a.SessionId == session.Id && a.QuestionId == question.Id
                        && a.IsCorrect == true && a.PlayerId != assignment.CopierPlayerId);
                    copierAnswer.PendingPoints = engine.PointsForRank(round.ConfigJson, rank);
                    copierAnswer.PointsAwarded = copierAnswer.PendingPoints;
                }
                else
                {
                    copierAnswer.PendingPoints = targetAnswer.PendingPoints;
                    copierAnswer.PointsAwarded = targetAnswer.PointsAwarded;
                }

                copierAnswer.TeamId = session.TeamScoringEnabled ? copier?.TeamId : null;
                copierAnswer.ValidatedByGmAt = DateTime.UtcNow;
            }

            db.CopyPasteAssignments.Remove(assignment);
        }

        return true;
    }
}
