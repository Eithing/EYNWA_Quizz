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

        // Sous-manches (thèmes) : le nettoyage complet passe déjà par le cascade Quiz -> Round (QuizId),
        // Restrict ici pour éviter un deuxième chemin de cascade sur la même table (auto-référence).
        modelBuilder.Entity<Round>()
            .HasOne(r => r.Parent)
            .WithMany(r => r.SubRounds)
            .HasForeignKey(r => r.ParentRoundId)
            .OnDelete(DeleteBehavior.Restrict);

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
    }
}
