using System.Text.Json;
using QuizParty.Api.Features.Qa;
using QuizParty.Api.Features.Shared;

namespace QuizParty.Api.Features.BlindTest;

/// <summary>
/// Moteur d'exécution de la feature "blind-test" : même principe que "qa-text" (même configuration de
/// manche — validation, buzzer, retry, scoring au rang…), seul le contenu de la question change (un
/// extrait audio à écouter plutôt qu'une question écrite). Hérite de QaEngine pour ne pas dupliquer
/// toute la mécanique de timing/validation/scoring, ne redéfinit que ce qui dépend du payload.
/// </summary>
public class BlindTestEngine : QaEngine
{
    public override string FeatureTypeKey => "blind-test";

    public override FeatureAnswerEvaluation Evaluate(
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

    public override string BuildPublicPayloadJson(string payloadJson)
    {
        var payload = ParsePayload(payloadJson);
        return JsonSerializer.Serialize(new { audioUrl = payload.AudioUrl }, PublicJsonOptions);
    }

    private static BlindTestQuestionPayload ParsePayload(string payloadJson) =>
        JsonSerializer.Deserialize<BlindTestQuestionPayload>(payloadJson, JsonOptions) ?? new BlindTestQuestionPayload();
}
