namespace QuizParty.Api.Models;

public enum GameSessionStatus
{
    Lobby,
    Running,
    Paused,
    /// <summary>Dernière question d'une manche terminée : en attente que le GM lance la manche suivante.</summary>
    RoundIntermission,
    /// <summary>La manche à venir est ciblée (Round.RequiresTargetPlayer) : en attente que le GM désigne le joueur concerné avant que le minuteur démarre.</summary>
    AwaitingTargetPlayer,
    Finished
}

public class GameSession
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    /// <summary>Généré à chaque lancement de session, utilisé comme lien d'invitation.</summary>
    public required string InviteToken { get; set; }

    public GameSessionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>-1 tant que la session est en Lobby (aucune manche démarrée).</summary>
    public int CurrentRoundIndex { get; set; } = -1;

    /// <summary>-1 tant qu'aucune question n'est active dans la manche courante.</summary>
    public int CurrentQuestionIndex { get; set; } = -1;

    /// <summary>Horodatage serveur de démarrage de la question courante — base du calcul du palier de zoom actif.</summary>
    public DateTime? CurrentQuestionStartedAt { get; set; }

    /// <summary>Non-null pendant une pause : sert à décaler CurrentQuestionStartedAt à la reprise pour ne pas pénaliser les joueurs.</summary>
    public DateTime? PausedAt { get; set; }

    /// <summary>Contrôlé explicitement par le GM : affiche le classement courant côté joueurs (en cours de partie ou en fin de partie).</summary>
    public bool ScoreboardVisible { get; set; }

    /// <summary>Joueur désigné par le GM pour la manche courante quand Round.RequiresTargetPlayer est vrai ; remis à null à chaque nouvelle manche.</summary>
    public int? CurrentRoundTargetPlayerId { get; set; }

    /// <summary>Joueur qui a buzzé le premier sur la question courante (mode buzzer de qa-text) ; remis à null à chaque nouvelle question et à chaque résolution du buzz par le GM.</summary>
    public int? CurrentBuzzHolderPlayerId { get; set; }

    public List<Player> Players { get; set; } = [];
}
