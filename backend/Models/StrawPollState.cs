namespace QuizParty.Api.Models;

/// <summary>
/// Outil utilitaire déclenché par l'hôte à tout moment de la partie (indépendant de GameSessionStatus) :
/// sondage à choix (vote unique ou multiple selon AllowMultipleVotes). Un seul outil (tirage OU strawpoll)
/// actif à la fois par session — IsClosed gate l'exclusivité.
/// </summary>
public class StrawPollState
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public string Question { get; set; } = "";

    /// <summary>Options du sondage (JSON List&lt;{Id,Text}&gt;).</summary>
    public string OptionsJson { get; set; } = "[]";

    public bool AllowMultipleVotes { get; set; }

    /// <summary>Contrôlé par l'hôte, même principe que GameSession.ScoreboardVisible : les résultats ne
    /// sont exposés aux joueurs qu'une fois ce flag activé.</summary>
    public bool ResultsRevealed { get; set; }

    /// <summary>Fermé par l'hôte : n'apparaît plus nulle part, permet de relancer un nouvel outil.</summary>
    public bool IsClosed { get; set; }

    /// <summary>IDs des joueurs concernés (JSON List&lt;int&gt;). Vide = tout le monde. Les équipes sont
    /// résolues en IDs joueurs à la création, jamais stockées telles quelles ici.</summary>
    public string ConcernedPlayerIdsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }

    public List<StrawPollVote> Votes { get; set; } = [];
}

/// <summary>Vote d'un joueur pour une option d'un StrawPollState (une ligne par option cochée : un vote
/// multiple se traduit par plusieurs lignes pour le même joueur).</summary>
public class StrawPollVote
{
    public int Id { get; set; }

    public int StrawPollStateId { get; set; }
    public StrawPollState? StrawPollState { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    public string OptionId { get; set; } = "";
    public DateTime SubmittedAt { get; set; }
}
