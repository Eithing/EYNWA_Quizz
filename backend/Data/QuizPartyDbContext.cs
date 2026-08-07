using Microsoft.EntityFrameworkCore;
using QuizParty.Api.Models;

namespace QuizParty.Api.Data;

public class QuizPartyDbContext(DbContextOptions<QuizPartyDbContext> options) : DbContext(options)
{
    public DbSet<GameMaster> GameMasters => Set<GameMaster>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<Round> Rounds => Set<Round>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Answer> Answers => Set<Answer>();
    public DbSet<ScoreAdjustment> ScoreAdjustments => Set<ScoreAdjustment>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<RoundParticipant> RoundParticipants => Set<RoundParticipant>();
    public DbSet<ThemeState> ThemeStates => Set<ThemeState>();
    public DbSet<RandomDrawState> RandomDrawStates => Set<RandomDrawState>();
    public DbSet<RandomDrawGuess> RandomDrawGuesses => Set<RandomDrawGuess>();
    public DbSet<StrawPollState> StrawPollStates => Set<StrawPollState>();
    public DbSet<StrawPollVote> StrawPollVotes => Set<StrawPollVote>();
    public DbSet<JokerGrant> JokerGrants => Set<JokerGrant>();
    public DbSet<JokerUsageEvent> JokerUsageEvents => Set<JokerUsageEvent>();
    public DbSet<CopyPasteAssignment> CopyPasteAssignments => Set<CopyPasteAssignment>();
    public DbSet<QcmFiftyFiftyReveal> QcmFiftyFiftyReveals => Set<QcmFiftyFiftyReveal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameMaster>()
            .HasIndex(g => g.DiscordId)
            .IsUnique();

        modelBuilder.Entity<Quiz>()
            .HasOne(q => q.Owner)
            .WithMany(g => g.Quizzes)
            .HasForeignKey(q => q.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Round>()
            .HasOne(r => r.Quiz)
            .WithMany(q => q.Rounds)
            .HasForeignKey(r => r.QuizId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Question>()
            .HasOne(q => q.Round)
            .WithMany(r => r.Questions)
            .HasForeignKey(q => q.RoundId)
            .OnDelete(DeleteBehavior.Cascade);

        // Sous-manches (thèmes) : Cascade ici aussi (en plus du cascade Quiz -> Round via QuizId) : sans ça, supprimer un quiz
        // contenant une manche à thèmes échoue avec "FOREIGN KEY constraint failed" — SQLite essaie de
        // supprimer la manche conteneur avant ses sous-manches, bloqué par la contrainte sur ParentRoundId
        // tant que les lignes enfants existent encore. SQLite (contrairement à SQL Server) n'interdit pas
        // les chemins de cascade multiples vers une même table.
        modelBuilder.Entity<Round>()
            .HasOne(r => r.Parent)
            .WithMany(r => r.SubRounds)
            .HasForeignKey(r => r.ParentRoundId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GameSession>()
            .HasIndex(s => s.InviteToken)
            .IsUnique();

        modelBuilder.Entity<GameSession>()
            .Property(s => s.Status)
            .HasConversion<string>();

        modelBuilder.Entity<GameSession>()
            .HasOne(s => s.Quiz)
            .WithMany()
            .HasForeignKey(s => s.QuizId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Player>()
            .HasOne(p => p.Session)
            .WithMany(s => s.Players)
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Player>()
            .HasIndex(p => p.ConnectionToken)
            .IsUnique();

        modelBuilder.Entity<Player>()
            .HasOne(p => p.Team)
            .WithMany(t => t.Players)
            .HasForeignKey(p => p.TeamId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Team>()
            .HasOne(t => t.Session)
            .WithMany(s => s.Teams)
            .HasForeignKey(t => t.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RoundParticipant>()
            .HasOne(rp => rp.Session)
            .WithMany()
            .HasForeignKey(rp => rp.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RoundParticipant>()
            .HasOne(rp => rp.Player)
            .WithMany()
            .HasForeignKey(rp => rp.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RoundParticipant>()
            .HasOne(rp => rp.Team)
            .WithMany()
            .HasForeignKey(rp => rp.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ThemeState>()
            .HasOne(t => t.Session)
            .WithMany()
            .HasForeignKey(t => t.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ThemeState>()
            .HasOne(t => t.SubRound)
            .WithMany()
            .HasForeignKey(t => t.SubRoundId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ThemeState>()
            .Property(t => t.Resolution)
            .HasConversion<string>();

        modelBuilder.Entity<Answer>()
            .Property(a => a.ValidationMode)
            .HasConversion<string>();

        modelBuilder.Entity<Answer>()
            .HasOne(a => a.Session)
            .WithMany()
            .HasForeignKey(a => a.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Answer>()
            .HasOne(a => a.Player)
            .WithMany()
            .HasForeignKey(a => a.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Answer>()
            .HasOne(a => a.Question)
            .WithMany()
            .HasForeignKey(a => a.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Answer>()
            .HasOne(a => a.Team)
            .WithMany()
            .HasForeignKey(a => a.TeamId)
            .OnDelete(DeleteBehavior.SetNull);

        // Pas d'index unique sur (PlayerId, QuestionId) : les manches à tentatives multiples (AllowRetry)
        // créent une ligne Answer par tentative.
        modelBuilder.Entity<Answer>()
            .HasIndex(a => new { a.PlayerId, a.QuestionId });

        modelBuilder.Entity<ScoreAdjustment>()
            .HasOne(a => a.Session)
            .WithMany()
            .HasForeignKey(a => a.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScoreAdjustment>()
            .HasOne(a => a.Player)
            .WithMany()
            .HasForeignKey(a => a.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScoreAdjustment>()
            .HasOne(a => a.Team)
            .WithMany()
            .HasForeignKey(a => a.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ScoreAdjustment>()
            .HasOne(a => a.Question)
            .WithMany()
            .HasForeignKey(a => a.QuestionId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RandomDrawState>()
            .Property(r => r.Mode)
            .HasConversion<string>();

        modelBuilder.Entity<RandomDrawState>()
            .HasOne(r => r.Session)
            .WithMany()
            .HasForeignKey(r => r.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RandomDrawGuess>()
            .HasOne(g => g.RandomDrawState)
            .WithMany(r => r.Guesses)
            .HasForeignKey(g => g.RandomDrawStateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RandomDrawGuess>()
            .HasOne(g => g.Player)
            .WithMany()
            .HasForeignKey(g => g.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Une seule devinette par joueur et par tirage.
        modelBuilder.Entity<RandomDrawGuess>()
            .HasIndex(g => new { g.RandomDrawStateId, g.PlayerId })
            .IsUnique();

        modelBuilder.Entity<StrawPollState>()
            .HasOne(p => p.Session)
            .WithMany()
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StrawPollVote>()
            .HasOne(v => v.StrawPollState)
            .WithMany(p => p.Votes)
            .HasForeignKey(v => v.StrawPollStateId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StrawPollVote>()
            .HasOne(v => v.Player)
            .WithMany()
            .HasForeignKey(v => v.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Un vote par joueur et par option (empêche un double-clic de compter deux fois la même option).
        modelBuilder.Entity<StrawPollVote>()
            .HasIndex(v => new { v.StrawPollStateId, v.PlayerId, v.OptionId })
            .IsUnique();

        modelBuilder.Entity<JokerGrant>()
            .Property(g => g.Type)
            .HasConversion<string>();

        modelBuilder.Entity<JokerGrant>()
            .HasOne(g => g.Session)
            .WithMany()
            .HasForeignKey(g => g.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<JokerGrant>()
            .HasOne(g => g.Player)
            .WithMany()
            .HasForeignKey(g => g.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<JokerGrant>()
            .HasOne(g => g.Team)
            .WithMany()
            .HasForeignKey(g => g.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<JokerUsageEvent>()
            .Property(e => e.Type)
            .HasConversion<string>();

        modelBuilder.Entity<JokerUsageEvent>()
            .HasOne(e => e.Session)
            .WithMany()
            .HasForeignKey(e => e.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<JokerUsageEvent>()
            .HasOne(e => e.ActorPlayer)
            .WithMany()
            .HasForeignKey(e => e.ActorPlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<JokerUsageEvent>()
            .HasOne(e => e.ActorTeam)
            .WithMany()
            .HasForeignKey(e => e.ActorTeamId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull (pas Cascade) : ActorPlayerId cascade déjà vers Players — un second chemin cascade
        // depuis la même entité vers la même table forcerait EF à arbitrer un ordre de suppression
        // ambigu (même principe que Answer.Team/ScoreAdjustment.Question, déjà en SetNull ailleurs).
        modelBuilder.Entity<JokerUsageEvent>()
            .HasOne(e => e.TargetPlayer)
            .WithMany()
            .HasForeignKey(e => e.TargetPlayerId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CopyPasteAssignment>()
            .HasOne(c => c.Session)
            .WithMany()
            .HasForeignKey(c => c.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CopyPasteAssignment>()
            .HasOne(c => c.Question)
            .WithMany()
            .HasForeignKey(c => c.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CopyPasteAssignment>()
            .HasOne(c => c.CopierPlayer)
            .WithMany()
            .HasForeignKey(c => c.CopierPlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull, même raisonnement que JokerUsageEvent.TargetPlayer ci-dessus : CopierPlayerId cascade
        // déjà vers Players.
        modelBuilder.Entity<CopyPasteAssignment>()
            .HasOne(c => c.TargetPlayer)
            .WithMany()
            .HasForeignKey(c => c.TargetPlayerId)
            .OnDelete(DeleteBehavior.SetNull);

        // Un seul copier/coller actif par (copieur, question) — une nouvelle utilisation remplace la précédente.
        modelBuilder.Entity<CopyPasteAssignment>()
            .HasIndex(c => new { c.QuestionId, c.CopierPlayerId })
            .IsUnique();

        modelBuilder.Entity<QcmFiftyFiftyReveal>()
            .HasOne(r => r.Session)
            .WithMany()
            .HasForeignKey(r => r.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QcmFiftyFiftyReveal>()
            .HasOne(r => r.Question)
            .WithMany()
            .HasForeignKey(r => r.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QcmFiftyFiftyReveal>()
            .HasOne(r => r.Player)
            .WithMany()
            .HasForeignKey(r => r.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Un seul tirage 50/50 par (joueur, question) — relancer le joker sur la même question réutilise
        // le même masquage déjà calculé plutôt que d'en tirer un nouveau (voir JokerService.UseFiftyFifty).
        modelBuilder.Entity<QcmFiftyFiftyReveal>()
            .HasIndex(r => new { r.QuestionId, r.PlayerId })
            .IsUnique();
    }
}
