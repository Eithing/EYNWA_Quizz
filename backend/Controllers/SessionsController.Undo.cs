using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuizParty.Api.Data;
using QuizParty.Api.Dtos;
using QuizParty.Api.Extensions;
using QuizParty.Api.Models;

namespace QuizParty.Api.Controllers;

public partial class SessionsController
{
    /// <summary>Sac de champs pour la sérialisation JSON de l'instantané — PAS une entité EF, juste ce qui
    /// est nécessaire pour restaurer les champs mutables de GameSession (identité/cycle de vie exclus :
    /// Id/QuizId/InviteToken/CreatedAt/ExpiresAt ne sont jamais touchés par une action de jeu).</summary>
    private class GameSessionSnapshotFields
    {
        public GameSessionStatus Status { get; set; }
        public int CurrentRoundIndex { get; set; }
        public int CurrentQuestionIndex { get; set; }
        public DateTime? CurrentQuestionStartedAt { get; set; }
        public DateTime? PausedAt { get; set; }
        public bool ScoreboardVisible { get; set; }
        public int? CurrentBuzzHolderPlayerId { get; set; }
        public bool TeamScoringEnabled { get; set; }
        public int? CurrentThemeSubRoundId { get; set; }
        public int? ExchangeUsedForThemeSubRoundId { get; set; }
        public int? CurrentAnswererPlayerId { get; set; }
        public int? AloneInTheWorldPlayerId { get; set; }
        public int? AloneInTheWorldTeamId { get; set; }
        public int? MeFirstHolderPlayerId { get; set; }
        public int? MeFirstHolderTeamId { get; set; }
        public int MeFirstQuestionsRemaining { get; set; }
        public bool MeFirstConsumedThisQuestion { get; set; }
    }

    private class RandomDrawStateSnapshot
    {
        public RandomDrawState State { get; set; } = null!;
        public List<RandomDrawGuess> Guesses { get; set; } = [];
    }

    private class StrawPollStateSnapshot
    {
        public StrawPollState State { get; set; } = null!;
        public List<StrawPollVote> Votes { get; set; } = [];
    }

    /// <summary>Tout l'état "en jeu" d'une session, hors Players/Teams (jamais touchés par les 4 actions
    /// annulables, et un nouveau joueur qui rejoint entre l'instantané et l'annulation ne doit pas être
    /// éjecté) et hors JokerUsageEvent (historique/toast uniquement, aucun impact sur l'état de jeu).</summary>
    private class SessionSnapshot
    {
        public GameSessionSnapshotFields Session { get; set; } = new();
        public List<RoundParticipant> RoundParticipants { get; set; } = [];
        public List<ThemeState> ThemeStates { get; set; } = [];
        public List<Answer> Answers { get; set; } = [];
        public List<ScoreAdjustment> ScoreAdjustments { get; set; } = [];
        public List<JokerGrant> JokerGrants { get; set; } = [];
        public List<CopyPasteAssignment> CopyPasteAssignments { get; set; } = [];
        public List<QcmFiftyFiftyReveal> QcmFiftyFiftyReveals { get; set; } = [];
        public List<RandomDrawStateSnapshot> RandomDrawStates { get; set; } = [];
        public List<StrawPollStateSnapshot> StrawPollStates { get; set; } = [];
    }

    /// <summary>Capture l'état ACTUEL (avant toute mutation) de la session dans un instantané annulable —
    /// à appeler en tout premier, avant que l'endpoint appelant ne touche quoi que ce soit. Un seul niveau
    /// d'annulation : upsert sur la ligne unique de la session, écrase un éventuel instantané précédent.
    /// Ne fait PAS son propre SaveChangesAsync : reste tracké, committé par le SaveChangesAsync déjà
    /// présent en fin de l'endpoint appelant pour que capture + action restent atomiques.</summary>
    private async Task SaveUndoSnapshotAsync(GameSession session)
    {
        var randomDrawStates = await db.RandomDrawStates.AsNoTracking()
            .Where(r => r.SessionId == session.Id)
            .ToListAsync();
        var randomDrawGuesses = await db.RandomDrawGuesses.AsNoTracking()
            .Where(g => g.RandomDrawState!.SessionId == session.Id)
            .ToListAsync();

        var strawPollStates = await db.StrawPollStates.AsNoTracking()
            .Where(p => p.SessionId == session.Id)
            .ToListAsync();
        var strawPollVotes = await db.StrawPollVotes.AsNoTracking()
            .Where(v => v.StrawPollState!.SessionId == session.Id)
            .ToListAsync();

        var snapshot = new SessionSnapshot
        {
            Session = new GameSessionSnapshotFields
            {
                Status = session.Status,
                CurrentRoundIndex = session.CurrentRoundIndex,
                CurrentQuestionIndex = session.CurrentQuestionIndex,
                CurrentQuestionStartedAt = session.CurrentQuestionStartedAt,
                PausedAt = session.PausedAt,
                ScoreboardVisible = session.ScoreboardVisible,
                CurrentBuzzHolderPlayerId = session.CurrentBuzzHolderPlayerId,
                TeamScoringEnabled = session.TeamScoringEnabled,
                CurrentThemeSubRoundId = session.CurrentThemeSubRoundId,
                ExchangeUsedForThemeSubRoundId = session.ExchangeUsedForThemeSubRoundId,
                CurrentAnswererPlayerId = session.CurrentAnswererPlayerId,
                AloneInTheWorldPlayerId = session.AloneInTheWorldPlayerId,
                AloneInTheWorldTeamId = session.AloneInTheWorldTeamId,
                MeFirstHolderPlayerId = session.MeFirstHolderPlayerId,
                MeFirstHolderTeamId = session.MeFirstHolderTeamId,
                MeFirstQuestionsRemaining = session.MeFirstQuestionsRemaining,
                MeFirstConsumedThisQuestion = session.MeFirstConsumedThisQuestion
            },
            RoundParticipants = await db.RoundParticipants.AsNoTracking().Where(rp => rp.SessionId == session.Id).ToListAsync(),
            ThemeStates = await db.ThemeStates.AsNoTracking().Where(t => t.SessionId == session.Id).ToListAsync(),
            Answers = await db.Answers.AsNoTracking().Where(a => a.SessionId == session.Id).ToListAsync(),
            ScoreAdjustments = await db.ScoreAdjustments.AsNoTracking().Where(a => a.SessionId == session.Id).ToListAsync(),
            JokerGrants = await db.JokerGrants.AsNoTracking().Where(g => g.SessionId == session.Id).ToListAsync(),
            CopyPasteAssignments = await db.CopyPasteAssignments.AsNoTracking().Where(c => c.SessionId == session.Id).ToListAsync(),
            QcmFiftyFiftyReveals = await db.QcmFiftyFiftyReveals.AsNoTracking().Where(r => r.SessionId == session.Id).ToListAsync(),
            RandomDrawStates = randomDrawStates
                .Select(state => new RandomDrawStateSnapshot { State = state, Guesses = randomDrawGuesses.Where(g => g.RandomDrawStateId == state.Id).ToList() })
                .ToList(),
            StrawPollStates = strawPollStates
                .Select(state => new StrawPollStateSnapshot { State = state, Votes = strawPollVotes.Where(v => v.StrawPollStateId == state.Id).ToList() })
                .ToList()
        };

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);

        var existing = await db.SessionUndoSnapshots.SingleOrDefaultAsync(s => s.SessionId == session.Id);
        if (existing is null)
        {
            db.SessionUndoSnapshots.Add(new SessionUndoSnapshot { SessionId = session.Id, SnapshotJson = json, CreatedAt = DateTime.UtcNow });
        }
        else
        {
            existing.SnapshotJson = json;
            existing.CreatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>Annule la toute dernière action annulable (Next / ChooseTheme / LaunchTheme / SkipTheme) —
    /// un seul niveau, l'instantané est consommé (supprimé) une fois restauré, pas de "refaire".</summary>
    [Authorize]
    [HttpPost("{id:int}/undo")]
    public async Task<ActionResult<GameSessionStateDto>> Undo(int id)
    {
        var loaded = await LoadOwnedSession(id, User.GetGameMasterId());
        if (loaded is null)
        {
            return NotFound();
        }

        var (session, quiz) = loaded.Value;

        var snapshotRow = await db.SessionUndoSnapshots.SingleOrDefaultAsync(s => s.SessionId == id);
        if (snapshotRow is null)
        {
            return BadRequest("Rien à annuler.");
        }

        var snapshot = JsonSerializer.Deserialize<SessionSnapshot>(snapshotRow.SnapshotJson, JsonOptions)
            ?? throw new InvalidOperationException("Instantané d'annulation illisible.");

        session.Status = snapshot.Session.Status;
        session.CurrentRoundIndex = snapshot.Session.CurrentRoundIndex;
        session.CurrentQuestionIndex = snapshot.Session.CurrentQuestionIndex;
        session.CurrentQuestionStartedAt = snapshot.Session.CurrentQuestionStartedAt;
        session.PausedAt = snapshot.Session.PausedAt;
        session.ScoreboardVisible = snapshot.Session.ScoreboardVisible;
        session.CurrentBuzzHolderPlayerId = snapshot.Session.CurrentBuzzHolderPlayerId;
        session.TeamScoringEnabled = snapshot.Session.TeamScoringEnabled;
        session.CurrentThemeSubRoundId = snapshot.Session.CurrentThemeSubRoundId;
        session.ExchangeUsedForThemeSubRoundId = snapshot.Session.ExchangeUsedForThemeSubRoundId;
        session.CurrentAnswererPlayerId = snapshot.Session.CurrentAnswererPlayerId;
        session.AloneInTheWorldPlayerId = snapshot.Session.AloneInTheWorldPlayerId;
        session.AloneInTheWorldTeamId = snapshot.Session.AloneInTheWorldTeamId;
        session.MeFirstHolderPlayerId = snapshot.Session.MeFirstHolderPlayerId;
        session.MeFirstHolderTeamId = snapshot.Session.MeFirstHolderTeamId;
        session.MeFirstQuestionsRemaining = snapshot.Session.MeFirstQuestionsRemaining;
        session.MeFirstConsumedThisQuestion = snapshot.Session.MeFirstConsumedThisQuestion;

        db.RoundParticipants.RemoveRange(await db.RoundParticipants.Where(rp => rp.SessionId == id).ToListAsync());
        db.RoundParticipants.AddRange(snapshot.RoundParticipants.Select(rp => new RoundParticipant
        {
            SessionId = id,
            PlayerId = rp.PlayerId,
            TeamId = rp.TeamId
        }));

        db.ThemeStates.RemoveRange(await db.ThemeStates.Where(t => t.SessionId == id).ToListAsync());
        db.ThemeStates.AddRange(snapshot.ThemeStates.Select(t => new ThemeState
        {
            SessionId = id,
            SubRoundId = t.SubRoundId,
            IsRevealed = t.IsRevealed,
            Resolution = t.Resolution
        }));

        db.Answers.RemoveRange(await db.Answers.Where(a => a.SessionId == id).ToListAsync());
        db.Answers.AddRange(snapshot.Answers.Select(a => new Answer
        {
            SessionId = id,
            PlayerId = a.PlayerId,
            QuestionId = a.QuestionId,
            RawAnswer = a.RawAnswer,
            IsCorrect = a.IsCorrect,
            PendingPoints = a.PendingPoints,
            PointsAwarded = a.PointsAwarded,
            TeamId = a.TeamId,
            ValidationMode = a.ValidationMode,
            ValidatedByGmAt = a.ValidatedByGmAt,
            SubmittedAt = a.SubmittedAt,
            IsFromCopyPasteJoker = a.IsFromCopyPasteJoker
        }));

        db.ScoreAdjustments.RemoveRange(await db.ScoreAdjustments.Where(a => a.SessionId == id).ToListAsync());
        db.ScoreAdjustments.AddRange(snapshot.ScoreAdjustments.Select(a => new ScoreAdjustment
        {
            SessionId = id,
            PlayerId = a.PlayerId,
            TeamId = a.TeamId,
            QuestionId = a.QuestionId,
            Delta = a.Delta,
            Reason = a.Reason,
            CreatedAt = a.CreatedAt
        }));

        db.JokerGrants.RemoveRange(await db.JokerGrants.Where(g => g.SessionId == id).ToListAsync());
        db.JokerGrants.AddRange(snapshot.JokerGrants.Select(g => new JokerGrant
        {
            SessionId = id,
            Type = g.Type,
            PlayerId = g.PlayerId,
            TeamId = g.TeamId,
            Charges = g.Charges,
            AllowedRoundIdsJson = g.AllowedRoundIdsJson
        }));

        db.CopyPasteAssignments.RemoveRange(await db.CopyPasteAssignments.Where(c => c.SessionId == id).ToListAsync());
        db.CopyPasteAssignments.AddRange(snapshot.CopyPasteAssignments.Select(c => new CopyPasteAssignment
        {
            SessionId = id,
            QuestionId = c.QuestionId,
            CopierPlayerId = c.CopierPlayerId,
            TargetPlayerId = c.TargetPlayerId,
            CreatedAt = c.CreatedAt
        }));

        db.QcmFiftyFiftyReveals.RemoveRange(await db.QcmFiftyFiftyReveals.Where(r => r.SessionId == id).ToListAsync());
        db.QcmFiftyFiftyReveals.AddRange(snapshot.QcmFiftyFiftyReveals.Select(r => new QcmFiftyFiftyReveal
        {
            SessionId = id,
            QuestionId = r.QuestionId,
            PlayerId = r.PlayerId,
            HiddenOptionIdsJson = r.HiddenOptionIdsJson
        }));

        // RemoveRange cascade automatiquement vers Guesses/Votes (voir QuizPartyDbContext) ; les nouvelles
        // instances sont réinsérées via leur propriété de navigation plutôt qu'une FK manuelle, pour que
        // EF résolve lui-même l'id parent fraîchement généré vers chaque enfant.
        db.RandomDrawStates.RemoveRange(await db.RandomDrawStates.Where(r => r.SessionId == id).ToListAsync());
        foreach (var rds in snapshot.RandomDrawStates)
        {
            db.RandomDrawStates.Add(new RandomDrawState
            {
                SessionId = id,
                Mode = rds.State.Mode,
                Label = rds.State.Label,
                MinValue = rds.State.MinValue,
                MaxValue = rds.State.MaxValue,
                ConcernedPlayerIdsJson = rds.State.ConcernedPlayerIdsJson,
                DrawnValue = rds.State.DrawnValue,
                IsResolved = rds.State.IsResolved,
                IsClosed = rds.State.IsClosed,
                CreatedAt = rds.State.CreatedAt,
                Guesses = rds.Guesses.Select(g => new RandomDrawGuess { PlayerId = g.PlayerId, GuessValue = g.GuessValue, SubmittedAt = g.SubmittedAt }).ToList()
            });
        }

        db.StrawPollStates.RemoveRange(await db.StrawPollStates.Where(p => p.SessionId == id).ToListAsync());
        foreach (var sps in snapshot.StrawPollStates)
        {
            db.StrawPollStates.Add(new StrawPollState
            {
                SessionId = id,
                Question = sps.State.Question,
                OptionsJson = sps.State.OptionsJson,
                AllowMultipleVotes = sps.State.AllowMultipleVotes,
                ResultsRevealed = sps.State.ResultsRevealed,
                IsClosed = sps.State.IsClosed,
                ConcernedPlayerIdsJson = sps.State.ConcernedPlayerIdsJson,
                CreatedAt = sps.State.CreatedAt,
                Votes = sps.Votes.Select(v => new StrawPollVote { PlayerId = v.PlayerId, OptionId = v.OptionId, SubmittedAt = v.SubmittedAt }).ToList()
            });
        }

        await db.SaveChangesAsync();

        // Consommé : un seul niveau d'annulation, pas de "refaire".
        db.SessionUndoSnapshots.Remove(snapshotRow);
        await db.SaveChangesAsync();

        await BroadcastState(session, quiz);
        return Ok(await BuildStateDto(session, quiz));
    }
}
