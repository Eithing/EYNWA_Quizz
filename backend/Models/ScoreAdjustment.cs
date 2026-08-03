namespace QuizParty.Api.Models;

/// <summary>Historique des corrections manuelles du GM (ajout/retrait/annulation de points), sur le score
/// perso d'un joueur ou sur le pot d'une équipe — exactement un des deux (PlayerId xor TeamId).</summary>
public class ScoreAdjustment
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public int? PlayerId { get; set; }
    public Player? Player { get; set; }

    public int? TeamId { get; set; }
    public Team? Team { get; set; }

    public int? QuestionId { get; set; }
    public Question? Question { get; set; }

    public int Delta { get; set; }
    public required string Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}
