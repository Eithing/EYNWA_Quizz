namespace Server.Models;

public class Quiz
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public GameMaster? Owner { get; set; }

    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string InviteCode { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public List<QuizStep> Steps { get; set; } = [];
}
