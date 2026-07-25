namespace Server.Models;

public class GameMaster
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public List<Quiz> Quizzes { get; set; } = [];
}
