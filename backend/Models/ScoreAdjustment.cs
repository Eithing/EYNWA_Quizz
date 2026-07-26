namespace QuizParty.Api.Models;

/// <summary>Historique des corrections manuelles du GM (ajout/retrait/annulation de points).</summary>
public class ScoreAdjustment
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    public int? QuestionId { get; set; }
    public Question? Question { get; set; }

    public int Delta { get; set; }
    public required string Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}
