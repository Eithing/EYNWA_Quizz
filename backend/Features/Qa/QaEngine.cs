using System.Text.Json;
using QuizParty.Api.Features.Shared;

namespace QuizParty.Api.Features.Qa;

/// <summary>
/// Moteur d'exécution de la feature "qa-text" (question écrite / réponse attendue). Contrairement à
/// zoom-image, pas de dégressivité dans le temps : les points sont fixes (ou basés sur le rang) tant que
/// la fenêtre de réponse est ouverte. Sert aussi de base à BlindTestEngine (même config, payload différent).
/// </summary>
public class QaEngine : IFeatureEngine
{
    protected static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    protected static readonly JsonSerializerOptions PublicJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public virtual string FeatureTypeKey => "qa-text";

    public FeatureRuntimeState ComputeState(string configJson, DateTime questionStartedAt, DateTime? pausedAt, DateTime now)
    {
        var config = ParseConfig(configJson);
        var elapsedSeconds = SessionTiming.ComputeElapsedSeconds(questionStartedAt, pausedAt, now);
        return ComputeStateAtElapsed(config, elapsedSeconds);
    }

    public int PointsForElapsedSeconds(string configJson, double elapsedSeconds) =>
        ComputeStateAtElapsed(ParseConfig(configJson), elapsedSeconds).CurrentPoints;

    public virtual FeatureAnswerEvaluation Evaluate(
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

        var isCorrect = payload.AcceptedAnswers.Any(accepted => AnswerMatcher.IsMatch(accepted, rawAnswer));
        return new FeatureAnswerEvaluation(isCorrect, isCorrect ? config.Points : 0);
    }

    public bool IsManualValidation(string configJson) => ParseConfig(configJson).ValidationMode == "Manual";

    /// <summary>En mode buzzer, gouverne le droit de re-buzzer après une mauvaise réponse (voir GetRetryCooldownSeconds).</summary>
    public bool AllowsRetryAfterWrongAnswer(string configJson) => ParseConfig(configJson).AllowRetry;

    /// <summary>Mode buzzer uniquement : délai avant de pouvoir re-buzzer après une mauvaise réponse. Sans effet sur la réponse écrite classique.</summary>
    public int GetRetryCooldownSeconds(string configJson)
    {
        var config = ParseConfig(configJson);
        return config.BuzzerMode ? config.BuzzerRetryCooldownSeconds : 0;
    }

    public bool IsBuzzerMode(string configJson) => ParseConfig(configJson).BuzzerMode;

    /// <summary>En mode buzzer, une seule bonne réponse suffit : la question est une course, pas un test collectif.</summary>
    public bool RequiresAllPlayersToAnswer(string configJson) => !ParseConfig(configJson).BuzzerMode;

    public virtual string BuildPublicPayloadJson(string payloadJson)
    {
        var payload = ParsePayload(payloadJson);
        return JsonSerializer.Serialize(new { questionText = payload.QuestionText }, PublicJsonOptions);
    }

    /// <summary>Pas de palier à révéler progressivement : une fois tout le monde répondu, plus rien à attendre.</summary>
    public double GetFastForwardTargetElapsedSeconds(string configJson) => ParseConfig(configJson).AnswerTimeSeconds;

    public bool UsesRankBasedScoring(string configJson) => ParseConfig(configJson).RankBasedScoring;

    public int PointsForRank(string configJson, int correctAnswerRank)
    {
        var config = ParseConfig(configJson);
        return Math.Max(0, config.RankMaxPoints - config.RankPointsDecrement * correctAnswerRank);
    }

    private static FeatureRuntimeState ComputeStateAtElapsed(QaRoundConfig config, double elapsedSeconds)
    {
        var secondsRemaining = (int)Math.Ceiling(Math.Max(0, config.AnswerTimeSeconds - elapsedSeconds));
        var isAnswerWindowOpen = elapsedSeconds < config.AnswerTimeSeconds;
        var shouldAutoAdvance = config.AutoAdvance && !isAnswerWindowOpen;
        // En scoring au rang, le nombre de points affiché ici (avant réponse) est le meilleur cas possible :
        // le rang exact dépend des réponses déjà validées des autres joueurs, connu seulement du contrôleur.
        var currentPoints = config.RankBasedScoring ? config.RankMaxPoints : config.Points;

        // Pas de palier : le temps restant "global" est le même que le temps restant du seul palier.
        return new FeatureRuntimeState(1, currentPoints, secondsRemaining, secondsRemaining, isAnswerWindowOpen, shouldAutoAdvance);
    }

    protected static QaRoundConfig ParseConfig(string configJson) =>
        JsonSerializer.Deserialize<QaRoundConfig>(configJson, JsonOptions) ?? new QaRoundConfig();

    private static QaQuestionPayload ParsePayload(string payloadJson) =>
        JsonSerializer.Deserialize<QaQuestionPayload>(payloadJson, JsonOptions) ?? new QaQuestionPayload();
}
