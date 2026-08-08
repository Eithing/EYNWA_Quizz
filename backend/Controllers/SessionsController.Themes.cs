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

    /// <summary>
    /// "À quoi pense l'autre" : compose à la volée un payload avec la réponse privée du répondant comme
    /// "réponse acceptée", pour que PartnerGuessEngine (qui hérite tel quel de QaEngine) évalue
    /// normalement la tentative du devineur. Choisit aussi le bon texte de question selon la phase (le
    /// répondant en phase 1 voit questionText, les devineurs en phase 2 voient guesserQuestionText s'il
    /// est renseigné, sinon on retombe sur questionText). Payload inchangé pour toute autre feature — le
    /// contrôleur reste agnostique de la feature dans tous les autres cas.
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

        string answererQuestionText;
        string? guesserQuestionText;
        try
        {
            var root = JsonDocument.Parse(question.PayloadJson).RootElement;
            answererQuestionText = root.TryGetProperty("questionText", out var qt) ? qt.GetString() ?? "" : "";
            guesserQuestionText = root.TryGetProperty("guesserQuestionText", out var gqt) ? gqt.GetString() : null;
        }
        catch
        {
            answererQuestionText = "";
            guesserQuestionText = null;
        }

        // Phase 1 = le seul participant désigné est le répondant lui-même (voir SetPartnerGuessAnswerer,
        // qui pose exactement ce RoundParticipant) ; StartPartnerGuessGuessing le remplace entièrement par
        // la sélection de devineurs pour passer en phase 2 — même critère que isPartnerGuessPhase1 côté
        // frontend (host-live.component.ts).
        var participants = await db.RoundParticipants.Where(rp => rp.SessionId == session.Id).ToListAsync();
        var isPhase1 = participants.Count == 1
            && participants[0].TeamId is null
            && participants[0].PlayerId == session.CurrentAnswererPlayerId;

        var questionText = isPhase1
            ? answererQuestionText
            : (string.IsNullOrEmpty(guesserQuestionText) ? answererQuestionText : guesserQuestionText);

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
}
