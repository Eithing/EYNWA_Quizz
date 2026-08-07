namespace QuizParty.Api.Models;

public enum JokerType
{
    Exchange,
    AloneInTheWorld,
    CopyPaste,
    MeFirst,
    FiftyFifty
}

/// <summary>Attribution d'un stock de charges d'un joker à un joueur OU une équipe (jamais les deux),
/// décidée par le GM en lobby. AllowedRoundIdsJson vide = utilisable sur toute manche compatible avec
/// ce type de joker (pas d'UI de restriction dans cette première version, le champ reste pour plus tard).</summary>
public class JokerGrant
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public JokerType Type { get; set; }

    public int? PlayerId { get; set; }
    public Player? Player { get; set; }

    public int? TeamId { get; set; }
    public Team? Team { get; set; }

    public int Charges { get; set; }

    public string AllowedRoundIdsJson { get; set; } = "[]";
}

/// <summary>Historique des jokers utilisés — alimente le toast temps réel (SignalR "JokerUsed") et un
/// petit flux consultable côté host, même principe que AnswerFeedDto.</summary>
public class JokerUsageEvent
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public JokerType Type { get; set; }

    public int? ActorPlayerId { get; set; }
    public Player? ActorPlayer { get; set; }

    public int? ActorTeamId { get; set; }
    public Team? ActorTeam { get; set; }

    public int? TargetPlayerId { get; set; }
    public Player? TargetPlayer { get; set; }

    /// <summary>Détail libre affiché dans le toast (ex: titre du thème volé pour Échange).</summary>
    public string? Detail { get; set; }

    public DateTime UsedAt { get; set; }
}

/// <summary>Copier/coller en attente de résolution — appliqué à la fermeture de la fenêtre de réponse de
/// QuestionId : la réponse du copieur devient une copie exacte de celle du joueur cible.</summary>
public class CopyPasteAssignment
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public int QuestionId { get; set; }
    public Question? Question { get; set; }

    public int CopierPlayerId { get; set; }
    public Player? CopierPlayer { get; set; }

    /// <summary>Nullable uniquement pour permettre SetNull si le joueur cible est supprimé (ligne
    /// historique orpheline mais sans blocage de suppression) — toujours renseigné à la création.</summary>
    public int? TargetPlayerId { get; set; }
    public Player? TargetPlayer { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>Options QCM masquées pour UN joueur sur UNE question précise (effet personnel du joker
/// Cinquante-cinquante, jamais partagé) — filtré dans PublicPayloadJson au moment de
/// GetCurrentQuestionForPlayer, aucun changement à QcmEngine.</summary>
public class QcmFiftyFiftyReveal
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public int QuestionId { get; set; }
    public Question? Question { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    public string HiddenOptionIdsJson { get; set; } = "[]";
}
