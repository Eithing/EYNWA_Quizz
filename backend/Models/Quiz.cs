namespace QuizParty.Api.Models;

public class Quiz
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public GameMaster? Owner { get; set; }

    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<Round> Rounds { get; set; } = [];
}
