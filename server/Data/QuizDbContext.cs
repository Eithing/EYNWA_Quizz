using Microsoft.EntityFrameworkCore;
using Server.Models;

namespace Server.Data;

public class QuizDbContext(DbContextOptions<QuizDbContext> options) : DbContext(options)
{
    public DbSet<GameMaster> GameMasters => Set<GameMaster>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizStep> QuizSteps => Set<QuizStep>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    public DbSet<QuizSession> QuizSessions => Set<QuizSession>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerAnswer> PlayerAnswers => Set<PlayerAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameMaster>()
            .HasIndex(g => g.Username)
            .IsUnique();

        modelBuilder.Entity<Quiz>()
            .HasIndex(q => q.InviteCode)
            .IsUnique();

        modelBuilder.Entity<Quiz>()
            .HasOne(q => q.Owner)
            .WithMany(g => g.Quizzes)
            .HasForeignKey(q => q.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuizStep>()
            .HasOne(s => s.Quiz)
            .WithMany(q => q.Steps)
            .HasForeignKey(s => s.QuizId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuizStep>()
            .Property(s => s.Type)
            .HasConversion<string>();

        modelBuilder.Entity<MediaAsset>()
            .HasOne(m => m.Owner)
            .WithMany()
            .HasForeignKey(m => m.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuizSession>()
            .Property(s => s.Status)
            .HasConversion<string>();

        modelBuilder.Entity<QuizSession>()
            .HasOne(s => s.Quiz)
            .WithMany()
            .HasForeignKey(s => s.QuizId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<QuizSession>()
            .HasMany(s => s.Players)
            .WithOne(p => p.Session)
            .HasForeignKey(p => p.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Player>()
            .HasIndex(p => p.ClientToken)
            .IsUnique();

        modelBuilder.Entity<PlayerAnswer>()
            .HasOne(a => a.Player)
            .WithMany()
            .HasForeignKey(a => a.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlayerAnswer>()
            .HasOne(a => a.QuizStep)
            .WithMany()
            .HasForeignKey(a => a.QuizStepId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PlayerAnswer>()
            .HasIndex(a => new { a.PlayerId, a.QuizStepId })
            .IsUnique();
    }
}
