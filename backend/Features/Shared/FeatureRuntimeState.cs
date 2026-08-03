namespace QuizParty.Api.Features.Shared;

/// <summary>État de jeu calculé pour la question courante, à un instant donné (jamais transmis par le client — recalculé serveur à chaque requête).</summary>
/// <param name="CurrentLevel">Progression visuelle propre à la feature (ex: niveau de zoom) ; vaut 1 pour les features qui n'en ont pas besoin.</param>
/// <param name="SecondsRemainingInStep">Temps restant avant le prochain palier (ou avant la fin, pour une feature sans palier).</param>
/// <param name="SecondsRemainingTotal">Temps restant avant la fermeture complète de la fenêtre de réponse, tous paliers confondus.</param>
public record FeatureRuntimeState(
    double CurrentLevel,
    int CurrentPoints,
    int SecondsRemainingInStep,
    int SecondsRemainingTotal,
    bool IsAnswerWindowOpen,
    bool ShouldAutoAdvance,
    /// <summary>Temps écoulé brut depuis le début de la question (gelé pendant une pause) — sert de position
    /// de lecture autoritaire pour synchroniser l'audio (blind-test) entre tous les clients.</summary>
    double SecondsElapsedTotal = 0);

public record FeatureAnswerEvaluation(bool? IsCorrect, int PointsAwarded);
