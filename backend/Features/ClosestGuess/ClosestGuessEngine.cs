using System.Text.Json;
using QuizParty.Api.Features.Shared;

namespace QuizParty.Api.Features.ClosestGuess;

/// <summary>
/// Moteur d'exécution de la feature "closest-guess" (estimation numérique, le plus proche de la vraie
/// valeur gagne). Contrairement à toutes les autres features, le classement ne peut être calculé qu'une
/// fois la fenêtre de réponse fermée — Evaluate() ne juge donc jamais une réponse individuellement
/// (toujours (null, 0)), la vraie évaluation se fait en lot dans SessionsController une fois toutes les
/// estimations reçues (voir DefersScoringUntilWindowClose).
/// </summary>
public class ClosestGuessEngine : IFeatureEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions PublicJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string FeatureTypeKey => "closest-guess";

    public FeatureRuntimeState ComputeState(string configJson, DateTime questionStartedAt, DateTime? pausedAt, DateTime now)
    {
        var config = ParseConfig(configJson);
        var elapsedSeconds = SessionTiming.ComputeElapsedSeconds(questionStartedAt, pausedAt, now);
        var secondsRemaining = (int)Math.Ceiling(Math.Max(0, config.AnswerTimeSeconds - elapsedSeconds));
        var isAnswerWindowOpen = elapsedSeconds < config.AnswerTimeSeconds;
        // Jamais d'avance automatique vers la question suivante : la révélation (essais de chacun, puis
        // gagnant) doit rester affichée le temps que le GM le décide, quel que soit AutoAdvance — sinon
        // le classement défile trop vite pour être vu (voir Next(), déclenché uniquement par le GM).
        var shouldAutoAdvance = false;
        // Meilleur cas possible avant résolution : personne ne connaît son rang tant que tout le monde n'a pas répondu.
        var currentPoints = config.RankBasedScoring ? config.RankMaxPoints : config.Points;

        return new FeatureRuntimeState(1, currentPoints, secondsRemaining, secondsRemaining, isAnswerWindowOpen, shouldAutoAdvance, elapsedSeconds);
    }

    public int PointsForElapsedSeconds(string configJson, double elapsedSeconds) => 0; // sans objet : le scoring se fait en lot, jamais au moment de la soumission.

    public FeatureAnswerEvaluation Evaluate(
        string configJson,
        string payloadJson,
        string rawAnswer,
        DateTime questionStartedAt,
        DateTime? pausedAt,
        DateTime submittedAt) => new(null, 0);

    public bool IsManualValidation(string configJson) => false;

    public string BuildPublicPayloadJson(string payloadJson)
    {
        var payload = ParsePayload(payloadJson);
        return JsonSerializer.Serialize(new { questionText = payload.QuestionText }, PublicJsonOptions);
    }

    public double GetFastForwardTargetElapsedSeconds(string configJson) => ParseConfig(configJson).AnswerTimeSeconds;

    // Le classement (dégressif ou "le premier seulement") est une forme de scoring au rang — voir PointsForRank.
    public bool UsesRankBasedScoring(string configJson) => true;

    /// <summary>Rang 0 = estimation la plus proche. Si RankBasedScoring est faux côté config, seul le rang 0 marque
    /// (Points fixes) — c'est un cas dégénéré de la même formule, pas un chemin de code séparé.</summary>
    public int PointsForRank(string configJson, int correctAnswerRank)
    {
        var config = ParseConfig(configJson);
        return config.RankBasedScoring
            ? RankScoring.PointsForRank(config.RankMaxPoints, config.RankPointsDecrement, correctAnswerRank)
            : (correctAnswerRank == 0 ? config.Points : 0);
    }

    public bool DefersScoringUntilWindowClose(string configJson) => true;

    public bool ShouldAutoResolveDeferredScoring(string configJson) => ParseConfig(configJson).ValidationMode != "Manual";

    public double? GetNumericTarget(string payloadJson) => ParsePayload(payloadJson).TargetValue;

    private static ClosestGuessRoundConfig ParseConfig(string configJson) =>
        JsonSerializer.Deserialize<ClosestGuessRoundConfig>(configJson, JsonOptions) ?? new ClosestGuessRoundConfig();

    private static ClosestGuessQuestionPayload ParsePayload(string payloadJson) =>
        JsonSerializer.Deserialize<ClosestGuessQuestionPayload>(payloadJson, JsonOptions) ?? new ClosestGuessQuestionPayload();
}
