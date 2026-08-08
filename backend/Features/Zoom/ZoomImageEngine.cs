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

        if (config.ValidationMode == "Manual")
        {
            return new FeatureAnswerEvaluation(null, 0);
        }

        var expectedAnswers = payload.ExpectedAnswersOrLegacy();
        var submittedAnswers = ExpectedAnswerMatching.SplitRawAnswer(rawAnswer, expectedAnswers.Count);

        // Mode "points personnalisés" : chaque réponse rapporte son montant fixe, la dégressivité du
        // dézoom ne s'applique pas (les deux mécaniques ne se cumulent pas, décision produit). Sinon
        // (Uniform), comportement historique : le palier de zoom courant fixe les points de la seule
        // réponse attendue. Le PointsMode de la question prime sur celui de la manche s'il est renseigné
        // (surcharge par question, voir ZoomQuestionPayload.PointsMode).
        var effectivePointsMode = payload.PointsMode ?? config.PointsMode;

        int EffectivePoints(ExpectedAnswer expected)
        {
            if (effectivePointsMode == "PerAnswer")
            {
                return expected.Points ?? 0;
            }

            var elapsedSeconds = SessionTiming.ComputeElapsedSeconds(questionStartedAt, pausedAt, submittedAt);
            return ComputeStateAtElapsed(config, elapsedSeconds).CurrentPoints;
        }

        var result = ExpectedAnswerMatching.Match(expectedAnswers, submittedAnswers, EffectivePoints);
        return new FeatureAnswerEvaluation(result.AllMatched, result.PointsAwarded);
    }

    public bool IsManualValidation(string configJson) => ParseConfig(configJson).ValidationMode == "Manual";

    public bool AllowsRetryAfterWrongAnswer(string configJson) => ParseConfig(configJson).AllowRetry;

    public int GetRetryCooldownSeconds(string configJson) => ParseConfig(configJson).RetryCooldownSeconds;

    public bool UsesRankBasedScoring(string configJson) => ParseConfig(configJson).RankBasedScoring;

    public int PointsForRank(string configJson, int correctAnswerRank)
    {
        var config = ParseConfig(configJson);
        return RankScoring.PointsForRank(config.RankMaxPoints, config.RankPointsDecrement, correctAnswerRank, config.RankMinPoints);
    }

    /// <summary>En mode "PerAnswer", les points sont fixés par la réponse attendue et ne dépendent pas du
    /// palier de zoom courant — mais Evaluate() ne calcule rien en validation manuelle (voir plus haut), donc
    /// sans ce hook ComputePointsIfCorrect (SessionsController) retomberait à tort sur le barème du dézoom.</summary>
    public int? FixedManualValidationPoints(string payloadJson, string configJson)
    {
        var payload = ParsePayload(payloadJson);
        var config = ParseConfig(configJson);
        if ((payload.PointsMode ?? config.PointsMode) != "PerAnswer")
        {
            return null;
        }

        var expectedAnswers = payload.ExpectedAnswersOrLegacy();
        return expectedAnswers.Count > 0 ? (expectedAnswers[0].Points ?? 0) : 0;
    }

    public string BuildPublicPayloadJson(string payloadJson) => BuildPublicPayloadJson(payloadJson, "{}");

    public string BuildPublicPayloadJson(string payloadJson, string configJson)
    {
        var payload = ParsePayload(payloadJson);
        var config = ParseConfig(configJson);
        var expectedAnswers = payload.ExpectedAnswersOrLegacy();

        // Le barème par réponse n'a de sens à afficher que si le mode effectif (surcharge de la question,
        // sinon celui de la manche) est "PerAnswer" : en Uniform, les points dépendent du palier de zoom
        // courant (déjà affiché ailleurs via currentPoints), pas d'un montant fixe par réponse — rien de
        // statique à montrer ici dans ce cas.
        var effectivePointsMode = payload.PointsMode ?? config.PointsMode;
        List<int>? expectedAnswerPoints = effectivePointsMode == "PerAnswer" && expectedAnswers.Count > 0
            ? ExpectedAnswerMatching.BuildPointsArray(expectedAnswers, e => e.Points ?? 0)
            : null;

        return JsonSerializer.Serialize(
            new
            {
                imageUrl = payload.ImageUrl,
                zoomFocusX = payload.ZoomFocusPoint.X,
                zoomFocusY = payload.ZoomFocusPoint.Y,
                expectedAnswerCount = Math.Max(1, expectedAnswers.Count),
                expectedAnswerPoints,
                comment = payload.Comment
            },
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
