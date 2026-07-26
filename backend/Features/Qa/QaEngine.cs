using System.Text.Json;
using QuizParty.Api.Features.Shared;

namespace QuizParty.Api.Features.Qa;

/// <summary>
/// Moteur d'exécution de la feature "qa-text" (question écrite / réponse attendue). Contrairement à
/// zoom-image, pas de dégressivité : les points sont fixes tant que la fenêtre de réponse est ouverte.
/// </summary>
public class QaEngine : IFeatureEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions PublicJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string FeatureTypeKey => "qa-text";

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

        var isCorrect = payload.AcceptedAnswers.Any(accepted => AnswerMatcher.IsMatch(accepted, rawAnswer));
        return new FeatureAnswerEvaluation(isCorrect, isCorrect ? config.Points : 0);
    }

    public bool IsManualValidation(string configJson) => ParseConfig(configJson).ValidationMode == "Manual";

    public bool IsBuzzerMode(string configJson) => ParseConfig(configJson).BuzzerMode;

    /// <summary>En mode buzzer, une seule bonne réponse suffit : la question est une course, pas un test collectif.</summary>
    public bool RequiresAllPlayersToAnswer(string configJson) => !ParseConfig(configJson).BuzzerMode;

    public string BuildPublicPayloadJson(string payloadJson)
    {
        var payload = ParsePayload(payloadJson);
        return JsonSerializer.Serialize(new { questionText = payload.QuestionText }, PublicJsonOptions);
    }

    /// <summary>Pas de palier à révéler progressivement : une fois tout le monde répondu, plus rien à attendre.</summary>
    public double GetFastForwardTargetElapsedSeconds(string configJson) => ParseConfig(configJson).AnswerTimeSeconds;

    private static FeatureRuntimeState ComputeStateAtElapsed(QaRoundConfig config, double elapsedSeconds)
    {
        var secondsRemaining = (int)Math.Ceiling(Math.Max(0, config.AnswerTimeSeconds - elapsedSeconds));
        var isAnswerWindowOpen = elapsedSeconds < config.AnswerTimeSeconds;
        var shouldAutoAdvance = config.AutoAdvance && !isAnswerWindowOpen;

        return new FeatureRuntimeState(1, config.Points, secondsRemaining, isAnswerWindowOpen, shouldAutoAdvance);
    }

    private static QaRoundConfig ParseConfig(string configJson) =>
        JsonSerializer.Deserialize<QaRoundConfig>(configJson, JsonOptions) ?? new QaRoundConfig();

    private static QaQuestionPayload ParsePayload(string payloadJson) =>
        JsonSerializer.Deserialize<QaQuestionPayload>(payloadJson, JsonOptions) ?? new QaQuestionPayload();
}
