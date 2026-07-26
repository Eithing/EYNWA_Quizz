namespace QuizParty.Api.Models;

public class Player
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public required string Pseudo { get; set; }

    /// <summary>Stocké côté client (localStorage) pour permettre la reconnexion sans perdre le score.</summary>
    public required Guid ConnectionToken { get; set; }

    public DateTime JoinedAt { get; set; }
}
