namespace Server.Models;

public class Player
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public QuizSession? Session { get; set; }

    public required string Name { get; set; }
    public int Score { get; set; }
    public required Guid ClientToken { get; set; }
    public DateTime JoinedAtUtc { get; set; }
}
