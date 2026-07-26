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
            .HasIndex(a => new { a.PlayerId, a.QuestionId })
            .IsUnique();

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
            .HasOne(a => a.Question)
            .WithMany()
            .HasForeignKey(a => a.QuestionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
