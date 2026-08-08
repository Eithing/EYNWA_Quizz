namespace QuizParty.Api.Models;

public enum GameSessionStatus
{
    Lobby,
    Running,
    Paused,
    /// <summary>Dernière question d'une manche terminée : en attente que le GM lance la manche suivante.</summary>
    RoundIntermission,
    /// <summary>La manche à venir est restreinte (Round.RestrictsParticipants) : en attente que le GM désigne les joueurs/équipes concernés avant que le minuteur démarre.</summary>
    AwaitingParticipants,
    /// <summary>Manche à thèmes en cours : les joueurs voient le plateau de thèmes, en attente que le GM en désigne un (ou skip).</summary>
    ChoosingTheme,
    /// <summary>Manche "à quoi pense l'autre" : en attente que le GM désigne le joueur qui répond en privé
    /// à la question courante, avant que quiconque tente de deviner sa réponse.</summary>
    AwaitingAnswerer,
    /// <summary>Des équipes existent pour cette session : en attente que le GM décide si la manche à venir
    /// se joue en mode équipe avant que le minuteur ne démarre.</summary>
    AwaitingTeamMode,
    /// <summary>Manche à thèmes : un thème vient d'être désigné et ses participants assignés, mais la
    /// manche n'a pas encore démarré (le minuteur n'est pas lancé) — fenêtre pendant laquelle le joker
    /// Échange peut voler la désignation. Le GM démarre explicitement via /themes/{subRoundId}/launch.</summary>
    ThemeReadyToLaunch,
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

    /// <summary>Joueur qui a buzzé le premier sur la question courante (mode buzzer de qa-text) ; remis à null à chaque nouvelle question et à chaque résolution du buzz par le GM.</summary>
    public int? CurrentBuzzHolderPlayerId { get; set; }

    /// <summary>Coché par le GM pour la manche/sous-manche courante : les points gagnés vont dans le pot
    /// d'équipe du joueur plutôt que dans son score perso. Remis à false à chaque nouvelle manche (activé
    /// automatiquement si le GM désigne une/des équipe(s) comme participants).</summary>
    public bool TeamScoringEnabled { get; set; }

    /// <summary>Manche à thèmes en cours : sous-manche actuellement active (Round.Id d'un enfant de la
    /// manche courante), null tant qu'aucun thème n'a été choisi ou qu'on n'est pas dans une manche à thèmes.</summary>
    public int? CurrentThemeSubRoundId { get; set; }

    /// <summary>Joker Échange déjà utilisé avec succès sur ce thème (SubRound.Id) — verrouille
    /// définitivement contre tout Échange suivant sur le même thème, pour éviter une guerre de vol
    /// sans fin entre équipes. Remis à null à chaque nouveau ChooseTheme.</summary>
    public int? ExchangeUsedForThemeSubRoundId { get; set; }

    /// <summary>Manche "à quoi pense l'autre" : joueur désigné par le GM pour répondre en privé à la
    /// question courante (peut changer à chaque question). Null tant qu'aucun n'est désigné.</summary>
    public int? CurrentAnswererPlayerId { get; set; }

    /// <summary>Joker Seul au monde : détenteur (joueur solo ou équipe) sur la question courante — remis à
    /// null à chaque nouvelle question, même cycle de vie que CurrentBuzzHolderPlayerId.</summary>
    public int? AloneInTheWorldPlayerId { get; set; }
    public int? AloneInTheWorldTeamId { get; set; }

    /// <summary>Joker Moi d'abord : détenteur du verrou buzzer (joueur solo ou équipe).</summary>
    public int? MeFirstHolderPlayerId { get; set; }
    public int? MeFirstHolderTeamId { get; set; }

    /// <summary>Nombre de questions encore couvertes par le verrou Moi d'abord (démarre à 2, décrémenté à
    /// chaque nouvelle question tant que non nul ; le détenteur est effacé quand ça atteint 0).</summary>
    public int MeFirstQuestionsRemaining { get; set; }

    /// <summary>Vrai dès que le détenteur du verrou Moi d'abord a buzzé sur la question courante — le
    /// verrou ne bloque alors plus les autres joueurs pour LE RESTE de cette question (retry classique si
    /// sa réponse est jugée fausse), remis à faux à chaque nouvelle question.</summary>
    public bool MeFirstConsumedThisQuestion { get; set; }

    public List<Player> Players { get; set; } = [];
    public List<Team> Teams { get; set; } = [];
}
