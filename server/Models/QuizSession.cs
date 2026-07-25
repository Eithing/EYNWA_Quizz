namespace Server.Models;

public enum SessionStatus
{
    Lobby,
    InProgress,
    Finished
}

public class QuizSession
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    public SessionStatus Status { get; set; }
    public int CurrentStepIndex { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public List<Player> Players { get; set; } = [];
}
