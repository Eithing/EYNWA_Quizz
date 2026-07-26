namespace QuizParty.Api.Models;

public enum AnswerValidationMode
{
    Auto,
    Manual
}

public class Answer
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public GameSession? Session { get; set; }

    public int PlayerId { get; set; }
    public Player? Player { get; set; }

    public int QuestionId { get; set; }
    public Question? Question { get; set; }

    public required string RawAnswer { get; set; }

    /// <summary>Null tant que la réponse n'a pas été jugée (auto ou manuel).</summary>
    public bool? IsCorrect { get; set; }

    /// <summary>
    /// Points que rapporterait cette réponse si jugée correcte, figés au palier de zoom actif
    /// au moment de l'envoi. Nécessaire pour valider manuellement une réponse plus tard sans
    /// dépendre de la position courante (déjà avancée) de la session.
    /// </summary>
    public int PendingPoints { get; set; }

    public int PointsAwarded { get; set; }
    public AnswerValidationMode ValidationMode { get; set; }
    public DateTime? ValidatedByGmAt { get; set; }
    public DateTime SubmittedAt { get; set; }
}
