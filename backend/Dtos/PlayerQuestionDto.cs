namespace QuizParty.Api.Dtos;

/// <summary>Un essai (closest-guess) tel que visible par tous les joueurs une fois la fenêtre de réponse
/// fermée — IsCorrect/PointsAwarded restent null tant que le GM (ou l'auto-résolution) n'a pas révélé le
/// classement.</summary>
public record ClosestGuessEntryDto(string PlayerPseudo, string RawAnswer, bool? IsCorrect, int? PointsAwarded);

/// <summary>Vue joueur d'une question, quelle que soit la feature : PublicPayloadJson est assaini par le moteur
/// de la feature concernée (jamais les réponses acceptées ni tout autre champ réservé au GM).</summary>
public record PlayerQuestionDto(
    int QuestionId,
    string RoundTitle,
    string FeatureTypeKey,
    string PublicPayloadJson,
    double CurrentLevel,
    int SecondsRemainingInStep,
    int SecondsRemainingTotal,
    bool IsAnswerWindowOpen,
    bool HasAnswered,
    List<string> CorrectFinders,
    bool IsSpectator,
    bool IsBuzzerMode,
    /// <summary>Position de lecture autoritaire pour l'audio (blind-test) — ignoré par les autres features.</summary>
    double SecondsElapsedTotal,
    /// <summary>Minuteur gelé (revue GM en cours ou pause explicite) : l'audio doit se figer pour tout le monde.</summary>
    bool IsPaused,
    /// <summary>Résultat de la dernière réponse DU JOUEUR pour cette question, une fois jugée — reste null
    /// tant qu'aucun verdict n'existe (utile pour les features à résolution différée type closest-guess,
    /// où SubmitAnswerResponse ne peut pas donner le résultat final au moment de l'envoi).</summary>
    bool? MyLastAnswerIsCorrect,
    int? MyLastAnswerPoints,
    /// <summary>closest-guess uniquement : tous les essais (pseudo + valeur soumise), visibles dès la
    /// fenêtre fermée — IsCorrect/PointsAwarded par entrée restent null tant que non résolu. Null pour
    /// toute autre feature, ou tant que la fenêtre est encore ouverte.</summary>
    List<ClosestGuessEntryDto>? ClosestGuessEntries,
    /// <summary>closest-guess uniquement : la vraie valeur, révélée seulement une fois le classement résolu.</summary>
    double? ClosestGuessTargetValue,
    /// <summary>order-list uniquement : ordre courant des IDs d'items du groupe du joueur (lui seul, ou
    /// toute son équipe si le mode équipe est actif) — mis à jour en temps quasi-réel à chaque
    /// glisser-déposer d'un membre du groupe. Null tant que spectateur ou hors fenêtre de réponse.</summary>
    List<string>? OrderListCurrentOrder,
    /// <summary>order-list uniquement : l'ordre correct, révélé seulement une fois le brouillon du groupe
    /// finalisé (clic "Valider" ou fermeture de la fenêtre).</summary>
    List<string>? OrderListCorrectOrder,
    /// <summary>order-list uniquement : IDs des items de OrderListCurrentOrder qui appartenaient à la plus
    /// longue chaîne bien enchaînée (voir LongestIncreasingSubsequence) — pour surligner côté client
    /// lesquels ont compté dans le score. Null tant que non finalisé.</summary>
    List<string>? OrderListChainItemIds,
    /// <summary>order-list uniquement : points obtenus par le groupe sur cette question, une fois finalisé.</summary>
    int? OrderListPointsAwarded);
