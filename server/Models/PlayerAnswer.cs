namespace Server.Models;

public class PlayerAnswer
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    public int QuizStepId { get; set; }
    public QuizStep? QuizStep { get; set; }

    public required string SubmittedAnswer { get; set; }
    public bool IsCorrect { get; set; }
    public int PointsAwarded { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
}
