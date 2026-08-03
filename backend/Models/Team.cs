namespace QuizParty.Api.Models;

/// <summary>Équipe créée en direct par le GM pour une session (pas au niveau du quiz : dépend des joueurs
/// réellement inscrits). Le score d'équipe est un pot séparé, additionné au score perso en fin de partie.</summary>
public class Team
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public required string Name { get; set; }

    public List<Player> Players { get; set; } = [];
}
