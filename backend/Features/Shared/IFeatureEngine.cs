namespace QuizParty.Api.Features.Shared;

/// <summary>
/// Contrat d'exécution d'une feature de quiz (section 4 de la spec) : chaque feature possède son propre
/// moteur, résolu par SessionsController via FeatureEngineRegistry en fonction de Round.FeatureTypeKey.
/// Round.ConfigJson / Question.PayloadJson restent des blobs opaques pour le contrôleur ; seul le moteur
/// de la feature concernée sait les interpréter.
/// </summary>
public interface IFeatureEngine
{
    string FeatureTypeKey { get; }

    FeatureRuntimeState ComputeState(string configJson, DateTime questionStartedAt, DateTime? pausedAt, DateTime now);

    /// <summary>Points qu'une réponse correcte rapporterait si elle était soumise maintenant (utilisé pour figer PendingPoints en mode Manual).</summary>
    int PointsForElapsedSeconds(string configJson, double elapsedSeconds);

    FeatureAnswerEvaluation Evaluate(
        string configJson,
        string payloadJson,
        string rawAnswer,
        DateTime questionStartedAt,
        DateTime? pausedAt,
        DateTime submittedAt);

    bool IsManualValidation(string configJson);

    /// <summary>Version de Question.PayloadJson sûre à envoyer aux joueurs (sans les réponses acceptées ni tout autre champ réservé au GM).</summary>
    string BuildPublicPayloadJson(string payloadJson);

    /// <summary>Question de rapidité (buzzer) : pas de saisie écrite, le GM valide directement le joueur qui a la main. Par défaut non applicable.</summary>
    bool IsBuzzerMode(string configJson) => false;

    /// <summary>
    /// Faut-il que TOUS les joueurs répondent correctement pour considérer la question "trouvée"
    /// (déclenche le saut vers la fin du suspense) ? Vrai par défaut ; faux pour un mode buzzer où
    /// une seule bonne réponse suffit à clore la question pour tout le monde.
    /// </summary>
    bool RequiresAllPlayersToAnswer(string configJson) => true;

    /// <summary>
    /// Temps écoulé (en secondes) à partir duquel il n'y a plus de "suspense" à préserver une fois que tout
    /// le monde a trouvé : la question saute directement à cet instant plutôt que de faire attendre les
    /// joueurs jusqu'au bout du minuteur normal (ex: zoom-image saute au palier final plutôt qu'à zéro).
    /// </summary>
    double GetFastForwardTargetElapsedSeconds(string configJson);
}
