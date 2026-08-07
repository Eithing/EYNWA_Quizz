namespace QuizParty.Api.Models;

public enum RandomDrawMode
{
    /// <summary>Tire et révèle immédiatement à tout le monde, aucune phase de devinette.</summary>
    Reveal,
    /// <summary>Les joueurs concernés devinent un nombre ; à la révélation, le plus proche gagne.</summary>
    GuessWinner,
    /// <summary>Les joueurs concernés devinent un nombre ; à la révélation, classement complet par distance.</summary>
    GuessRanking
}

/// <summary>
/// Outil utilitaire déclenché par l'hôte à tout moment de la partie (indépendant de GameSessionStatus) :
/// tirage d'un nombre aléatoire, affiché directement (Reveal) ou après une phase de devinette (Guess*).
/// Un seul outil (tirage OU strawpoll) actif à la fois par session — IsClosed gate l'exclusivité.
/// </summary>
public class RandomDrawState
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public RandomDrawMode Mode { get; set; }

    /// <summary>Pourquoi ce tirage, affiché aux joueurs (ex: "Qui commence ?").</summary>
    public string Label { get; set; } = "";

    public int MinValue { get; set; }
    public int MaxValue { get; set; }

    /// <summary>IDs des joueurs concernés (JSON List&lt;int&gt;). Vide = tout le monde. Les équipes sont
    /// résolues en IDs joueurs à la création, jamais stockées telles quelles ici.</summary>
    public string ConcernedPlayerIdsJson { get; set; } = "[]";

    /// <summary>Null tant que non tiré (modes Guess* avant /reveal).</summary>
    public int? DrawnValue { get; set; }

    public bool IsResolved { get; set; }

    /// <summary>Fermé par l'hôte : n'apparaît plus nulle part, permet de relancer un nouvel outil.</summary>
    public bool IsClosed { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<RandomDrawGuess> Guesses { get; set; } = [];
}

/// <summary>Devinette d'un joueur pour un RandomDrawState en mode GuessWinner/GuessRanking.</summary>
public class RandomDrawGuess
{
    public int Id { get; set; }

    public int RandomDrawStateId { get; set; }
    public RandomDrawState? RandomDrawState { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    public int GuessValue { get; set; }
    public DateTime SubmittedAt { get; set; }
}
