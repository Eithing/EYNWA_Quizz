namespace QuizParty.Api.Models;

/// <summary>Le "User" de la spec (section 5) : un Game Master authentifié via Discord.</summary>
public class GameMaster
{
    public int Id { get; set; }
    public required string DiscordId { get; set; }
    public required string Username { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<Quiz> Quizzes { get; set; } = [];
}
