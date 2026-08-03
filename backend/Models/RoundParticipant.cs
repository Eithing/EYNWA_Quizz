namespace QuizParty.Api.Models;

/// <summary>
/// Participant sélectionné par le GM pour la manche courante d'une session (Round.RestrictsParticipants),
/// ou pour la sous-manche en cours d'une manche à thèmes. Exactement un des deux (PlayerId xor TeamId) est
/// renseigné par ligne — mode équipe si TeamId, mode joueurs sinon. Effacé et repeuplé à chaque nouvelle
/// désignation par le GM (une par manche/sous-manche restreinte).
/// </summary>
public class RoundParticipant
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public int? PlayerId { get; set; }
    public Player? Player { get; set; }

    public int? TeamId { get; set; }
    public Team? Team { get; set; }
}
