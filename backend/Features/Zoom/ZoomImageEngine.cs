using System.Text.Json;
using QuizParty.Api.Features.Shared;

namespace QuizParty.Api.Features.Zoom;

/// <summary>
/// Moteur d'exécution de la feature "zoom-image" (section 6). Le serveur est seul autoritaire
/// sur le temps : le palier actif est toujours recalculé depuis CurrentQuestionStartedAt, jamais
/// transmis par le client.
/// </summary>
public class ZoomImageEngine : IFeatureEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions PublicJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string FeatureTypeKey => "zoom-image";

    public FeatureRuntimeState ComputeState(string configJson, DateTime questionStartedAt, DateTime? pausedAt, DateTime now)
    {
        var config = ParseConfig(configJson);
        var elapsedSeconds = SessionTiming.ComputeElapsedSeconds(questionStartedAt, pausedAt, now);
        return ComputeStateAtElapsed(config, elapsedSeconds);
    }

    public int PointsForElapsedSeconds(string configJson, double elapsedSeconds) =>
        ComputeStateAtElapsed(ParseConfig(configJson), elapsedSeconds).CurrentPoints;

    public FeatureAnswerEvaluation Evaluate(
        string configJson,
        string payloadJson,
        string rawAnswer,
        DateTime questionStartedAt,
        DateTime? pausedAt,
        DateTime submittedAt)
    {
        var config = ParseConfig(configJson);
        var payload = ParsePayload(payloadJson);
        var elapsedSeconds = SessionTiming.ComputeElapsedSeconds(questionStartedAt, pausedAt, submittedAt);
        var state = ComputeStateAtElapsed(config, elapsedSeconds);

        if (config.ValidationMode == "Manual")
        {
            return new FeatureAnswerEvaluation(null, 0);
        }

        var isCorrect = payload.AcceptedAnswers.Any(accepted => AnswerMatcher.IsMatch(accepted, rawAnswer));
        return new FeatureAnswerEvaluation(isCorrect, isCorrect ? state.CurrentPoints : 0);
    }

    public bool IsManualValidation(string configJson) => ParseConfig(configJson).ValidationMode == "Manual";

    public bool AllowsRetryAfterWrongAnswer(string configJson) => ParseConfig(configJson).AllowRetry;

    public bool UsesRankBasedScoring(string configJson) => ParseConfig(configJson).RankBasedScoring;

    public int PointsForRank(string configJson, int correctAnswerRank)
    {
        var config = ParseConfig(configJson);
        return Math.Max(0, config.RankMaxPoints - config.RankPointsDecrement * correctAnswerRank);
    }

    public string BuildPublicPayloadJson(string payloadJson)
    {
        var payload = ParsePayload(payloadJson);
        return JsonSerializer.Serialize(
            new { imageUrl = payload.ImageUrl, zoomFocusX = payload.ZoomFocusPoint.X, zoomFocusY = payload.ZoomFocusPoint.Y },
            PublicJsonOptions);
    }

    /// <summary>Le "suspense" du dézoom se termine au début du palier final : au-delà, on ne fait plus que patienter, rien de nouveau à révéler.</summary>
    public double GetFastForwardTargetElapsedSeconds(string configJson) =>
        ParseConfig(configJson).ZoomSteps.Sum(s => s.DurationSeconds);

    private static FeatureRuntimeState ComputeStateAtElapsed(ZoomRoundConfig config, double elapsedSeconds)
    {
        var cumulative = 0.0;
        ZoomStep? activeStep = null;
        var secondsRemainingInStep = 0;

        foreach (var step in config.ZoomSteps)
        {
            if (elapsedSeconds < cumulative + step.DurationSeconds)
            {
                activeStep = step;
                secondsRemainingInStep = (int)Math.Ceiling(cumulative + step.DurationSeconds - elapsedSeconds);
                break;
            }
            cumulative += step.DurationSeconds;
        }

        // Paliers épuisés : l'image reste au niveau final, les points restent plafonnés à ceux du dernier palier.
        var lastStep = config.ZoomSteps.Count > 0 ? config.ZoomSteps[^1] : null;
        var currentLevel = activeStep?.Level ?? config.FinalLevel;
        // En scoring au rang, le nombre de points affiché ici (avant réponse) est le meilleur cas possible :
        // le rang exact dépend des réponses déjà validées des autres joueurs, connu seulement du contrôleur.
        var currentPoints = config.RankBasedScoring ? config.RankMaxPoints : (activeStep?.Points ?? lastStep?.Points ?? 0);

        // AnswerTimeSeconds est un temps SUPPLÉMENTAIRE accordé une fois la séquence de zoom
        // terminée (pas un plafond concurrent) : sinon un answerTimeSeconds plus court que la
        // durée totale des paliers coupait le dézoom en plein milieu avant que le joueur ait
        // même vu l'image se révéler complètement.
        var totalZoomDuration = config.ZoomSteps.Sum(s => s.DurationSeconds);
        var totalRoundDuration = totalZoomDuration + config.AnswerTimeSeconds;

        var isAnswerWindowOpen = elapsedSeconds < totalRoundDuration;
        var shouldAutoAdvance = config.AutoAdvance && !isAnswerWindowOpen;
        var secondsRemainingTotal = (int)Math.Ceiling(Math.Max(0, totalRoundDuration - elapsedSeconds));

        return new FeatureRuntimeState(currentLevel, currentPoints, secondsRemainingInStep, secondsRemainingTotal, isAnswerWindowOpen, shouldAutoAdvance, elapsedSeconds);
    }

    private static ZoomRoundConfig ParseConfig(string configJson) =>
        JsonSerializer.Deserialize<ZoomRoundConfig>(configJson, JsonOptions) ?? new ZoomRoundConfig();

    private static ZoomQuestionPayload ParsePayload(string payloadJson) =>
        JsonSerializer.Deserialize<ZoomQuestionPayload>(payloadJson, JsonOptions) ?? new ZoomQuestionPayload();
}
