namespace QuizParty.Api.Models;

public class Player
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public required string Pseudo { get; set; }

    /// <summary>Équipe assignée par le GM pour cette session (composition libre, ex: 2 équipes de 3) ; null tant qu'aucune équipe n'a été créée ou que ce joueur n'y est pas rattaché.</summary>
    public int? TeamId { get; set; }
    public Team? Team { get; set; }

    /// <summary>Stocké côté client (localStorage) pour permettre la reconnexion sans perdre le score.</summary>
    public required Guid ConnectionToken { get; set; }

    public DateTime JoinedAt { get; set; }
}
