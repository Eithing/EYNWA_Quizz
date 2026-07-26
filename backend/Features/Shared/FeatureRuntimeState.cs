namespace QuizParty.Api.Features.Shared;

/// <summary>État de jeu calculé pour la question courante, à un instant donné (jamais transmis par le client — recalculé serveur à chaque requête).</summary>
/// <param name="CurrentLevel">Progression visuelle propre à la feature (ex: niveau de zoom) ; vaut 1 pour les features qui n'en ont pas besoin.</param>
public record FeatureRuntimeState(
    double CurrentLevel,
    int CurrentPoints,
    int SecondsRemainingInStep,
    bool IsAnswerWindowOpen,
    bool ShouldAutoAdvance);

public record FeatureAnswerEvaluation(bool? IsCorrect, int PointsAwarded);
